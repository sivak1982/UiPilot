using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WpfPilot.Cli.Tools;

/// <summary>
/// Out-of-process lifecycle tools: discover running apps, attach, and drive the AI edit loop by
/// building/launching/restarting the target app. These do not require an attached app to start.
/// </summary>
[McpServerToolType]
public sealed class LifecycleTools
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly ConnectionManager _connection;

    public LifecycleTools(ConnectionManager connection) => _connection = connection;

    [McpServerTool(Name = "list_apps")]
    [Description("List running WpfPilot-enabled apps discovered from %TEMP%/wpfpilot.")]
    public string ListApps()
    {
        var apps = _connection.ListAlive()
            .Select(a => new { a.Pid, a.ProcessName, a.MainWindowTitle, a.ProtocolVersion, a.StartedUtc })
            .ToList();
        return JsonSerializer.Serialize(new { count = apps.Count, apps }, Json);
    }

    [McpServerTool(Name = "attach")]
    [Description("Attach to a running WpfPilot app. If pid is omitted and exactly one app is running, attaches to it.")]
    public async Task<string> Attach(
        [Description("Process id of the target app. Optional when only one app is running.")] int? pid = null,
        CancellationToken ct = default)
    {
        var info = await _connection.AttachAsync(pid, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { attached = true, info.Pid, info.ProcessName, info.MainWindowTitle }, Json);
    }

    [McpServerTool(Name = "build_and_start")]
    [Description("Build a WPF project and launch it with WpfPilot enabled, then attach. This is the entry point of the edit loop.")]
    public async Task<string> BuildAndStart(
        [Description("Path to the .csproj (or a directory/solution the SDK can build) of the WPF app.")] string project,
        [Description("Build configuration.")] string configuration = "Debug",
        CancellationToken ct = default)
    {
        var info = await _connection.BuildAndStartAsync(project, configuration, ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { started = true, info.Pid, info.ProcessName, info.MainWindowTitle }, Json);
    }

    [McpServerTool(Name = "restart_app")]
    [Description("Rebuild and relaunch the last app started via build_and_start, then re-attach. Use after code edits.")]
    public async Task<string> RestartApp(CancellationToken ct = default)
    {
        var info = await _connection.RestartAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { restarted = true, info.Pid, info.ProcessName, info.MainWindowTitle }, Json);
    }

    [McpServerTool(Name = "stop_app")]
    [Description("Stop the app started via build_and_start and detach.")]
    public string StopApp()
    {
        _connection.StopApp();
        return JsonSerializer.Serialize(new { stopped = true }, Json);
    }
}
