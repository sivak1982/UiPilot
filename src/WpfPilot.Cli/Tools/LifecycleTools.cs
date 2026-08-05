using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using WpfPilot.Cli.Pipe;
using WpfPilot.Tools;

namespace WpfPilot.Cli.Tools;

/// <summary>
/// Out-of-process lifecycle tools: discover running apps, attach, and drive the AI edit loop by
/// building/launching/restarting the target app. These do not require an attached app to start.
/// </summary>
[McpServerToolType]
public sealed class LifecycleTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConnectionManager _connection;

    public LifecycleTools(ConnectionManager connection) => _connection = connection;

    [McpServerTool(Name = "list_apps")]
    [Description("List running pilot-enabled apps (WPF or Avalonia) discovered from %TEMP%/wpfpilot.")]
    public string ListApps()
    {
        var apps = _connection.ListAlive()
            .Select(a => new { a.Pid, a.ProcessName, a.MainWindowTitle, a.ProtocolVersion, a.StartedUtc, a.UiFramework })
            .ToList();
        return JsonSerializer.Serialize(new { count = apps.Count, apps }, Json);
    }

    [McpServerTool(Name = "attach")]
    [Description("Attach to a running pilot app (WPF or Avalonia). If pid is omitted, optional processName/uiFramework filters are applied before requiring exactly one match.")]
    public async Task<string> Attach(
        [Description("Process id of the target app. Optional when filters identify exactly one app.")] int? pid = null,
        [Description("Optional case-insensitive substring filter for process name when pid is omitted.")] string? processName = null,
        [Description("Optional exact UI framework filter when pid is omitted, e.g. 'wpf' or 'avalonia'.")] string? uiFramework = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.AttachAsync(pid, processName, uiFramework, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { attached = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json);
        }
        catch (Exception ex) when (TrySerializeToolError(ex, out var json))
        {
            return json;
        }
    }

    [McpServerTool(Name = "build_and_start")]
    [Description("Build a WPF or Avalonia project and launch it with pilot enabled, then attach. This is the entry point of the edit loop.")]
    public async Task<string> BuildAndStart(
        [Description("Path to the .csproj (or a directory/solution the SDK can build) of the target app.")] string project,
        [Description("Build configuration.")] string configuration = "Debug",
        [Description("Optional MSBuild platform (e.g. 'x64') for projects that require an explicit platform.")] string? platform = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.BuildAndStartAsync(project, configuration, platform, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { started = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json);
        }
        catch (Exception ex) when (TrySerializeToolError(ex, out var json))
        {
            return json;
        }
    }

    [McpServerTool(Name = "restart_app")]
    [Description("Rebuild and relaunch the last WPF or Avalonia app started via build_and_start, then re-attach. Use after code edits.")]
    public async Task<string> RestartApp(CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.RestartAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { restarted = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json);
        }
        catch (Exception ex) when (TrySerializeToolError(ex, out var json))
        {
            return json;
        }
    }

    [McpServerTool(Name = "detach")]
    [Description("Detach from the currently driven WPF or Avalonia app without stopping or killing its process.")]
    public string Detach()
    {
        _connection.Detach();
        return JsonSerializer.Serialize(new { detached = true }, Json);
    }

    [McpServerTool(Name = "stop_app")]
    [Description("Stop the currently driven app (launched via build_and_start or attached to) and detach. Terminating an elevated app requires this CLI to run elevated.")]
    public string StopApp()
    {
        _connection.StopApp();
        return JsonSerializer.Serialize(new { stopped = true }, Json);
    }

    private static bool TrySerializeToolError(Exception ex, out string json)
    {
        switch (ex)
        {
            case PilotCliException cli:
                json = ErrorJson(cli.Code, cli.Message, cli.Hint);
                return true;
            case PipeRpcException pipe:
                json = ErrorJson(pipe.Code ?? $"rpc_{pipe.RpcCode}", pipe.Message, pipe.Hint);
                return true;
            case InvalidOperationException invalid:
                json = ErrorJson(PilotErrorCodes.NotAttached, invalid.Message);
                return true;
            case TimeoutException timeout:
                json = ErrorJson("timeout", timeout.Message);
                return true;
            default:
                json = "";
                return false;
        }
    }

    private static string ErrorJson(string code, string message, string? hint = null) =>
        JsonSerializer.Serialize(new { error = true, code, message, hint }, Json);
}
