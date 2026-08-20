using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Client;
using UiPilot.Client.Pipe;
using UiPilot.Tools;

namespace UiPilot.Cli.Tools;

/// <summary>
/// Out-of-process lifecycle tools: discover running apps, attach named sessions, and drive the
/// AI edit loop by building/launching/restarting target apps. These do not require an attached
/// app to start. Multiple sessions are supported (e.g. a server UI and a client UI).
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

    [McpServerTool(Name = "list_sessions")]
    [Description("List attached sessions: pilot (MCP UI pipe) and process (launch tracking only). Includes which session is active and whether restart_app can relaunch it.")]
    public CallToolResult ListSessions()
    {
        var sessions = _connection.ListSessions();
        return Ok(JsonSerializer.Serialize(new
        {
            count = sessions.Count,
            activeSession = _connection.ActiveSessionName,
            sessions,
        }, Json));
    }

    [McpServerTool(Name = "select_session")]
    [Description("Set the sticky active session used by forwarding tools when they omit the session argument.")]
    public CallToolResult SelectSession(
        [Description("Session name from list_sessions / attach / start_app.")] string session)
    {
        try
        {
            var info = _connection.SelectSession(session);
            return Ok(JsonSerializer.Serialize(new { selected = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "attach")]
    [Description("Attach to a running pilot app (WPF, Avalonia, or WinForms) as a named session. If pid is omitted, optional processName/uiFramework filters are applied before requiring exactly one match. Does not disconnect other sessions.")]
    public async Task<CallToolResult> Attach(
        [Description("Process id of the target app. Optional when filters identify exactly one app.")] int? pid = null,
        [Description("Optional case-insensitive substring filter for process name when pid is omitted.")] string? processName = null,
        [Description("Optional exact UI framework filter when pid is omitted: 'wpf', 'avalonia', or 'winforms'.")] string? uiFramework = null,
        [Description("Optional session name. Defaults to the process name. Use short names like 'sim' and 'oi' when driving two apps.")] string? session = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.AttachAsync(pid, processName, uiFramework, session, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { attached = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "build_and_start")]
    [Description("Build a WPF, Avalonia, or WinForms project and launch it with UiPilot enabled (DOTNET_STARTUP_HOOKS by default), then attach as a named session. Only replaces an existing session with the same name; other sessions stay up.")]
    public async Task<CallToolResult> BuildAndStart(
        [Description("Path to the .csproj (or a directory/solution the SDK can build) of the target app.")] string project,
        [Description("Build configuration.")] string configuration = "Debug",
        [Description("Optional MSBuild platform (e.g. 'x64') for projects that require an explicit platform.")] string? platform = null,
        [Description("Optional session name. Defaults to the built assembly name.")] string? session = null,
        [Description("When true, start the app visible and pull it to the foreground instead of starting minimized. Use when a human is watching the run.")] bool foreground = false,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.BuildAndStartAsync(project, configuration, platform, session, foreground, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { started = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "start_app")]
    [Description("Launch a prebuilt .exe/.dll (no rebuild) with DOTNET_STARTUP_HOOKS injection by default, then attach as a named session. Target app need not call PilotHost.Start. Use for sample Bin folders when binaries already exist.")]
    public async Task<CallToolResult> StartApp(
        [Description("Path to the app .exe or .dll.")] string path,
        [Description("Optional session name. Defaults to the file name without extension (e.g. 'sim').")] string? session = null,
        [Description("Optional working directory. Defaults to the directory containing the app.")] string? workingDirectory = null,
        [Description("When true (default), set process-scoped DOTNET_STARTUP_HOOKS so UiPilot starts without editing the app. Set false if the app already calls PilotHost.Start and you want hook injection off.")] bool useStartupHook = true,
        [Description("Optional UI stack override: 'wpf', 'avalonia', or 'winforms'. When omitted, the generic hook selects the first live UI at runtime.")] string? uiFramework = null,
        [Description("When true, start the app visible and pull it to the foreground instead of starting minimized. Use when a human is watching the run.")] bool foreground = false,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.StartAppAsync(path, session, workingDirectory, useStartupHook, uiFramework, foreground, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { started = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "start_process")]
    [Description("Launch a non-pilot process (console host, helper, etc.) and track it as a named process session. Does not wait for UiPilot discovery. Pair with wait_for_log for readiness.")]
    public async Task<CallToolResult> StartProcess(
        [Description("Path to the .exe or .dll.")] string path,
        [Description("Optional session name. Defaults to the file name without extension.")] string? session = null,
        [Description("Optional working directory. Defaults to the directory containing the exe.")] string? workingDirectory = null,
        [Description("Optional process arguments string.")] string? arguments = null,
        [Description("When true (default), the process gets its own console window so it shows in the taskbar and its output stays out of this CLI's stdout. Set false to inherit this console.")] bool showWindow = true,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.StartProcessAsync(path, session, workingDirectory, arguments, showWindow, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { started = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "wait_for_log")]
    [Description("Poll a log file (or the newest file matching a glob/directory) until a regex matches. Generic readiness helper — supply path and pattern; not app-specific.")]
    public async Task<CallToolResult> WaitForLog(
        [Description("File path, directory (newest file), or simple glob like C:\\logs\\20260811\\*.log.")] string pathOrGlob,
        [Description(".NET regular expression to match in the log content (e.g. 'Startup completed').")] string pattern,
        [Description("Maximum wait time in milliseconds.")] int timeoutMs = 60_000,
        [Description("Delay between polls in milliseconds.")] int pollMs = 200,
        [Description("When true, only content written after the waiter first opens the file is searched. Default false searches the whole file.")] bool fromEnd = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _connection.WaitForLogAsync(pathOrGlob, pattern, timeoutMs, pollMs, fromEnd, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new
            {
                matched = true,
                path = result.Path,
                pattern = result.Pattern,
                match = result.Match,
                byteOffset = result.ByteOffset,
                elapsedMs = result.ElapsedMs,
            }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "restart_app")]
    [Description("Relaunch a session previously started via build_and_start, start_app, or start_process. Attached-only pilot sessions cannot be restarted.")]
    public async Task<CallToolResult> RestartApp(
        [Description("Optional session name. Defaults to the active session, or the only session.")] string? session = null,
        CancellationToken ct = default)
    {
        try
        {
            var info = await _connection.RestartAsync(session, ct).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { restarted = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    /// <summary>
    /// Drops only the active (or named) pipe attachment. Launch metadata is retained when the
    /// CLI started the process so restart_app can still relaunch after a detach.
    /// </summary>
    [McpServerTool(Name = "detach")]
    [Description("Detach a session's pipe connection without stopping or killing its process. Other sessions are left alone.")]
    public CallToolResult Detach(
        [Description("Optional session name. Defaults to the active session, or the only session.")] string? session = null)
    {
        try
        {
            var info = _connection.Detach(session);
            return Ok(JsonSerializer.Serialize(new { detached = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    /// <summary>
    /// Terminates one launched/attached session process and clears that session.
    /// </summary>
    [McpServerTool(Name = "stop_app")]
    [Description("Stop one driven session (pilot or process), including any processes it spawned, and clear it. When multiple sessions exist, pass session or select_session first. Terminating an elevated app requires this CLI to run elevated.")]
    public CallToolResult StopApp(
        [Description("Optional session name. Defaults to the active session, or the only session.")] string? session = null)
    {
        try
        {
            var info = _connection.StopApp(session);
            return Ok(JsonSerializer.Serialize(new { stopped = true, session = info }, Json));
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "stop_all")]
    [Description("Stop every driven session (pilot and process), including any processes they spawned, and clear all sessions.")]
    public CallToolResult StopAll()
    {
        try
        {
            var sessions = _connection.StopAll();
            return Ok(JsonSerializer.Serialize(new { stopped = true, count = sessions.Count, sessions }, Json));
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
