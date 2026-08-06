using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Cli.Pipe;
using UiPilot.Tools;

namespace UiPilot.Cli.Tools;

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
    [Description("List running pilot-enabled apps (WPF or Avalonia) discovered from %TEMP%/uipilot.")]
    public CallToolResult ListApps()
    {
        var apps = _connection.ListAlive()
            .Select(a => new { a.Pid, a.ProcessName, a.MainWindowTitle, a.ProtocolVersion, a.StartedUtc, a.UiFramework })
            .ToList();
        return Ok(JsonSerializer.Serialize(new { count = apps.Count, apps }, Json));
    }

    [McpServerTool(Name = "attach")]
    [Description("Attach to a running pilot app (WPF or Avalonia). If pid is omitted, optional processName/uiFramework filters are applied before requiring exactly one match.")]
    public async Task<CallToolResult> Attach(
        [Description("Process id of the target app. Optional when filters identify exactly one app.")] int? pid = null,
        [Description("Optional case-insensitive substring filter for process name when pid is omitted.")] string? processName = null,
        [Description("Optional exact UI framework filter when pid is omitted, e.g. 'wpf' or 'avalonia'.")] string? uiFramework = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.AttachAsync(pid, processName, uiFramework, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { attached = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "build_and_start")]
    [Description("Build a WPF or Avalonia project and launch it with pilot enabled, then attach. This is the entry point of the edit loop.")]
    public async Task<CallToolResult> BuildAndStart(
        [Description("Path to the .csproj (or a directory/solution the SDK can build) of the target app.")] string project,
        [Description("Build configuration.")] string configuration = "Debug",
        [Description("Optional MSBuild platform (e.g. 'x64') for projects that require an explicit platform.")] string? platform = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.BuildAndStartAsync(project, configuration, platform, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { started = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "restart_app")]
    [Description("Rebuild and relaunch the last WPF or Avalonia app started via build_and_start, then re-attach. Use after code edits.")]
    public async Task<CallToolResult> RestartApp(CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.RestartAsync(ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { restarted = true, info.Pid, info.ProcessName, info.MainWindowTitle, info.UiFramework }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    /// <summary>
    /// Drops only the active pipe attachment. The launched process and last build settings are
    /// retained so restart_app can still rebuild and relaunch after a detach.
    /// </summary>
    [McpServerTool(Name = "detach")]
    [Description("Detach from the currently driven WPF or Avalonia app without stopping or killing its process.")]
    public CallToolResult Detach()
    {
        try
        {
            _connection.Detach();
            return Ok(JsonSerializer.Serialize(new { detached = true }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    /// <summary>
    /// Terminates the process launched by build_and_start and/or the currently attached process,
    /// then clears the active attachment state.
    /// </summary>
    [McpServerTool(Name = "stop_app")]
    [Description("Stop the currently driven app (launched via build_and_start or attached to) and detach. Terminating an elevated app requires this CLI to run elevated.")]
    public CallToolResult StopApp()
    {
        try
        {
            _connection.StopApp();
            return Ok(JsonSerializer.Serialize(new { stopped = true }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
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
            case TimeoutException timeout:
                json = ErrorJson(PilotErrorCodes.Timeout, timeout.Message);
                return true;
            default:
                json = "";
                return false;
        }
    }

    private static bool TryCreateErrorResult(Exception ex, out CallToolResult result)
    {
        if (!TrySerializeToolError(ex, out var json))
        {
            result = new CallToolResult();
            return false;
        }

        result = new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = json },
            },
        };
        return true;
    }

    private static CallToolResult Ok(string text) => new()
    {
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = text },
        },
    };

    private static string ErrorJson(string code, string message, string? hint = null) =>
        JsonSerializer.Serialize(new { error = true, code, message, hint }, Json);
}
