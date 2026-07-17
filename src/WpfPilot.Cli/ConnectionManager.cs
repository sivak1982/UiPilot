using System.Text.Json;
using WpfPilot.Cli.Discovery;
using WpfPilot.Cli.Pipe;
using WpfPilot.Cli.Process;

namespace WpfPilot.Cli;

/// <summary>
/// Central state for the CLI: which app we're attached to, the live pipe connection, and the
/// process we launched (for build/restart). Registered as a DI singleton and injected into tools.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly DiscoveryReader _discovery = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private PipeClient? _client;
    private DiscoveryInfo? _current;
    private System.Diagnostics.Process? _launched;
    private string? _lastProject;
    private string? _lastConfiguration;

    public IReadOnlyList<DiscoveryInfo> ListAlive() => _discovery.ListAlive();

    public DiscoveryInfo? Current => _current;

    public async Task<DiscoveryInfo> AttachAsync(int? pid, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = ResolveTarget(pid);
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
            return await client.SendAsync(method, args, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            ResetConnection();
            throw new InvalidOperationException("Lost connection to the app (it may have exited). Re-attach or restart.");
        }
    }

    public async Task<DiscoveryInfo> BuildAndStartAsync(string project, string configuration, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ResetConnection();
            AppLauncher.KillTree(_launched);
            _launched = null;

            var targetPath = await AppLauncher.BuildAsync(project, configuration, ct).ConfigureAwait(false);
            _launched = AppLauncher.Start(targetPath);
            _lastProject = project;
            _lastConfiguration = configuration;

            var info = await WaitForDiscoveryAsync(_launched.Id, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await ConnectLocked(info, ct).ConfigureAwait(false);
            return info;
        }
        finally { _gate.Release(); }
    }

    public async Task<DiscoveryInfo> RestartAsync(CancellationToken ct = default)
    {
        if (_lastProject == null || _lastConfiguration == null)
            throw new InvalidOperationException("Nothing to restart. Use build_and_start first.");
        return await BuildAndStartAsync(_lastProject, _lastConfiguration, ct).ConfigureAwait(false);
    }

    public void StopApp()
    {
        ResetConnection();
        AppLauncher.KillTree(_launched);
        _launched = null;
    }

    private async Task<PipeClient> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true }) return _client;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is { IsConnected: true }) return _client;
            var target = ResolveTarget(null);
            await ConnectLocked(target, ct).ConfigureAwait(false);
            return _client!;
        }
        finally { _gate.Release(); }
    }

    private DiscoveryInfo ResolveTarget(int? pid)
    {
        var alive = _discovery.ListAlive();
        if (pid.HasValue)
        {
            var match = alive.FirstOrDefault(a => a.Pid == pid.Value)
                ?? throw new InvalidOperationException($"No running WpfPilot app with pid {pid.Value}.");
            return match;
        }
        if (alive.Count == 0)
            throw new InvalidOperationException("No running WpfPilot apps found. Use build_and_start or launch one, then attach.");
        if (alive.Count > 1)
            throw new InvalidOperationException($"Multiple apps running ({alive.Count}). Call attach with an explicit pid.");
        return alive[0];
    }

    private async Task ConnectLocked(DiscoveryInfo target, CancellationToken ct)
    {
        ResetConnection();
        _client = await PipeClient.ConnectAsync(target.PipeName, target.Token, 5000, ct).ConfigureAwait(false);
        _current = target;
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
                throw new InvalidOperationException($"App process {pid} exited before publishing a discovery file.");
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        throw new TimeoutException($"Timed out waiting for app {pid} to publish its discovery file.");
    }

    private void ResetConnection()
    {
        try { _client?.Dispose(); } catch { /* ignore */ }
        _client = null;
        _current = null;
    }

    public void Dispose()
    {
        ResetConnection();
        _gate.Dispose();
    }
}
