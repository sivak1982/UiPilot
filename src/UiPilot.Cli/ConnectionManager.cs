using System.Text.Json;
using UiPilot.Cli.Discovery;
using UiPilot.Cli.Pipe;
using UiPilot.Cli.Process;
using UiPilot.Server;
using UiPilot.Tools;

namespace UiPilot.Cli;

/// <summary>
/// Central state for the CLI: which app we're attached to, the live MCP-over-pipe connection, and
/// the process we launched (for build/restart). Registered as a DI singleton and injected into tools.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly DiscoveryReader _discovery = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private McpPipeClient? _client;
    private DiscoveryInfo? _current;
    private System.Diagnostics.Process? _launched;
    private string? _lastProject;
    private string? _lastConfiguration;
    private string? _lastPlatform;
    private int? _attachedPid;

    public IReadOnlyList<DiscoveryInfo> ListAlive() => _discovery.ListAlive();

    public DiscoveryInfo? Current => _current;

    public async Task<DiscoveryInfo> AttachAsync(
        int? pid,
        string? processName = null,
        string? uiFramework = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = ResolveTarget(pid, processName, uiFramework);
            await ConnectLocked(target, ct).ConfigureAwait(false);
            return target;
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonElement> SendAsync(string method, object? args, CancellationToken ct = default)
    {
        var client = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await client.PingAsync(ct).ConfigureAwait(false);
                return JsonSerializer.SerializeToElement(new { pong = true });
            }

            if (string.Equals(method, "describe", StringComparison.OrdinalIgnoreCase))
                return await client.ListToolsAsync(ct).ConfigureAwait(false);

            return await client.CallToolAsync(method, args, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            ResetConnection();
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                "Lost connection to the app (it may have exited). Re-attach or restart.");
        }
        catch (PipeRpcException ex)
        {
            throw new PilotCliException(
                ex.Code ?? $"rpc_{ex.RpcCode}",
                ex.Message,
                ex.Hint,
                ex);
        }
    }

    public async Task<DiscoveryInfo> BuildAndStartAsync(string project, string configuration, string? platform, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Project path is required.");
        if (string.IsNullOrWhiteSpace(configuration))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Build configuration is required.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            KillCurrentLocked();

            var targetPath = await AppLauncher.BuildAsync(project, configuration, platform, ct).ConfigureAwait(false);
            _launched = AppLauncher.Start(targetPath);
            _lastProject = project;
            _lastConfiguration = configuration;
            _lastPlatform = platform;

            var info = await WaitForDiscoveryAsync(_launched.Id, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await ConnectLocked(info, ct).ConfigureAwait(false);
            return info;
        }
        finally { _gate.Release(); }
    }

    public async Task<DiscoveryInfo> RestartAsync(CancellationToken ct = default)
    {
        if (_lastProject == null || _lastConfiguration == null)
            throw new PilotCliException(
                PilotErrorCodes.InvalidArgs,
                "Nothing to restart. Use build_and_start first.");
        return await BuildAndStartAsync(_lastProject, _lastConfiguration, _lastPlatform, ct).ConfigureAwait(false);
    }

    public void StopApp()
    {
        _gate.Wait();
        try { KillCurrentLocked(); }
        finally { _gate.Release(); }
    }

    public void Detach()
    {
        _gate.Wait();
        try { ResetConnection(); }
        finally { _gate.Release(); }
    }

    private void KillCurrentLocked()
    {
        var attachedPid = _attachedPid ?? _current?.Pid;
        ResetConnection();
        AppLauncher.KillTree(_launched);
        _launched = null;
        if (attachedPid.HasValue)
            AppLauncher.KillByPid(attachedPid.Value);
    }

    private async Task<McpPipeClient> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true }) return _client;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true }) return _client;
            ResetConnection();
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                "No app is attached.",
                "Use attach with an explicit target or build_and_start before calling app tools.");
        }
        finally { _gate.Release(); }
    }

    private DiscoveryInfo ResolveTarget(int? pid, string? processName = null, string? uiFramework = null)
    {
        var alive = _discovery.ListAlive();
        if (pid.HasValue)
        {
            var match = alive.FirstOrDefault(a => a.Pid == pid.Value)
                ?? throw new PilotCliException(
                    PilotErrorCodes.NotFound,
                    $"No running UiPilot app with pid {pid.Value}.",
                    "Use list_apps to see currently discoverable apps.");
            return match;
        }

        var filtered = alive.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(processName))
        {
            filtered = filtered.Where(a =>
                a.ProcessName.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (!string.IsNullOrWhiteSpace(uiFramework))
        {
            filtered = filtered.Where(a =>
                string.Equals(a.UiFramework, uiFramework, StringComparison.OrdinalIgnoreCase));
        }
        alive = filtered.ToList();

        if (alive.Count == 0)
            throw new PilotCliException(
                PilotErrorCodes.NotFound,
                TargetNotFoundMessage(processName, uiFramework),
                "Use list_apps to see currently discoverable apps, or build_and_start to launch one.");
        if (alive.Count > 1)
            throw new PilotCliException(
                PilotErrorCodes.Ambiguous,
                $"Multiple apps match ({alive.Count}). Call attach with an explicit pid or narrower filters.",
                "Use list_apps, then pass pid or narrower processName/uiFramework filters to attach.");
        return alive[0];
    }

    private static string TargetNotFoundMessage(string? processName, string? uiFramework)
    {
        if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(uiFramework))
            return "No running UiPilot apps found. Use build_and_start or launch one, then attach.";

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(processName))
            filters.Add($"processName contains '{processName}'");
        if (!string.IsNullOrWhiteSpace(uiFramework))
            filters.Add($"uiFramework equals '{uiFramework}'");
        return "No running UiPilot apps matched " + string.Join(" and ", filters) + ".";
    }

    private async Task ConnectLocked(DiscoveryInfo target, CancellationToken ct)
    {
        ResetConnection();
        _client = await McpPipeClient.ConnectAsync(target.PipeName, target.Token, 5000, ct).ConfigureAwait(false);
        _current = target;
        _attachedPid = target.Pid;
    }

    private async Task<DiscoveryInfo> WaitForDiscoveryAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var info = _discovery.FindByPid(pid);
            if (info != null) return info;
            if (_launched is { HasExited: true })
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"App process {pid} exited before publishing a discovery file.",
                    "Check the app startup failure, then run build_and_start again.");
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new TimeoutException($"Timed out waiting for app {pid} to publish its discovery file.");
    }

    private void ResetConnection()
    {
        try { _client?.Dispose(); } catch { /* ignore */ }
        _client = null;
        _current = null;
        _attachedPid = null;
    }

    public void Dispose()
    {
        ResetConnection();
        _gate.Dispose();
    }
}
