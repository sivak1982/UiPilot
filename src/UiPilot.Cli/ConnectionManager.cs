using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiPilot.Client.Discovery;
using UiPilot.Client.Pipe;
using UiPilot.Client.Process;
using UiPilot.Server;
using UiPilot.Tools;

namespace UiPilot.Client;

/// <summary>
/// Snapshot of an attached pilot session or a tracked non-pilot process session.
/// </summary>
public sealed class SessionSnapshot
{
    public required string Name { get; init; }
    /// <summary><c>pilot</c> (MCP pipe) or <c>process</c> (launch tracking only).</summary>
    public required string Kind { get; init; }
    public required bool IsActive { get; init; }
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }
    public required string? MainWindowTitle { get; init; }
    public required string? UiFramework { get; init; }
    public required bool LaunchedByCli { get; init; }
    public required bool CanRestart { get; init; }
}

/// <summary>
/// Central state for the CLI: named sessions to pilot-enabled apps, live MCP-over-pipe
/// connections, and processes launched for build/start/restart. Supports driving multiple
/// apps (e.g. a server UI and a client UI) at once.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);
    private const int PipeConnectTimeoutMs = 5_000;
    private const int DiscoveryInitialPollMs = 50;
    private const int DiscoveryMaxPollMs = 500;

    private readonly DiscoveryReader _discovery = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, AppSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _activeSession;

    public IReadOnlyList<DiscoveryInfo> ListAlive() => _discovery.ListAlive();

    /// <summary>Name of the sticky active session, if any.</summary>
    public string? ActiveSessionName
    {
        get
        {
            _gate.Wait();
            try
            {
                return _activeSession;
            }
            finally { _gate.Release(); }
        }
    }

    public IReadOnlyList<SessionSnapshot> ListSessions()
    {
        _gate.Wait();
        try
        {
            PruneExitedSessionsLocked();
            return _sessions.Values
                .Where(s => !s.Exited)
                .Select(s => ToSnapshot(s, string.Equals(s.Name, _activeSession, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    internal ConnectionStateSnapshot CaptureSnapshot()
    {
        // Discovery is filesystem I/O. Read it once and outside the session gate, then use that
        // same view both to prune attach-only sessions and to project status apps.
        var apps = _discovery.ListAlive();
        var alivePids = apps.Select(app => app.Pid).ToHashSet();
        _gate.Wait();
        try
        {
            PruneExitedSessionsLocked(alivePids);
            var sessions = _sessions.Values
                .Where(session => !session.Exited)
                .Select(session => ToSnapshot(
                    session,
                    string.Equals(session.Name, _activeSession, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new ConnectionStateSnapshot(_activeSession, sessions, apps);
        }
        finally { _gate.Release(); }
    }

    public SessionSnapshot SelectSession(string session)
    {
        if (string.IsNullOrWhiteSpace(session))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Session name is required.");

        _gate.Wait();
        try
        {
            var target = RequireSessionLocked(session);
            _activeSession = target.Name;
            return ToSnapshot(target, isActive: true);
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionSnapshot> AttachAsync(
        int? pid,
        string? processName = null,
        string? uiFramework = null,
        string? session = null,
        CancellationToken ct = default)
    {
        var info = ResolveTarget(pid, processName, uiFramework);
        var sessionName = ResolveSessionName(session, info.ProcessName);
        return await ConnectSessionAsync(
            sessionName, info, launched: null, launchSource: null, ct).ConfigureAwait(false);
    }

    public async Task<JsonElement> SendAsync(string method, object? args, string? session = null, CancellationToken ct = default)
    {
        McpPipeClient client;
        string sessionName;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = ResolveSessionForSendLocked(session);
            if (target.Client is not { IsConnected: true } connected)
            {
                ResetSessionConnectionLocked(target);
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"Session '{target.Name}' is not connected.",
                    "Use attach, start_app, or build_and_start for that session.");
            }

            client = connected;
            sessionName = target.Name;
        }
        finally { _gate.Release(); }

        try
        {
            if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await client.PingAsync(ct).ConfigureAwait(false);
                return JsonSerializer.SerializeToElement(new { pong = true, session = sessionName });
            }

            if (string.Equals(method, "describe", StringComparison.OrdinalIgnoreCase))
            {
                var described = await client.ListToolsAsync(ct).ConfigureAwait(false);
                return WrapWithSession(described, sessionName);
            }

            var result = await client.CallToolAsync(method, args, ct).ConfigureAwait(false);
            return WrapWithSession(result, sessionName);
        }
        catch (ObjectDisposedException ex)
        {
            await MarkDisconnectedAsync(sessionName, ct).ConfigureAwait(false);
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                $"Session '{sessionName}' was disposed during the call.",
                "Use attach, start_app, or build_and_start for that session.",
                ex);
        }
        catch (IOException)
        {
            await MarkDisconnectedAsync(sessionName, ct).ConfigureAwait(false);
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                $"Lost connection to session '{sessionName}' (the app may have exited). Re-attach or restart.",
                "Use list_sessions / list_apps, then attach or start_app again.");
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

    private async Task MarkDisconnectedAsync(string sessionName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessions.TryGetValue(sessionName, out var target))
                ResetSessionConnectionLocked(target);
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionSnapshot> BuildAndStartAsync(
        string project,
        string configuration,
        string? platform,
        string? session = null,
        bool foreground = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Project path is required.");
        if (string.IsNullOrWhiteSpace(configuration))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Build configuration is required.");

        var targetPath = await AppLauncher.BuildAsync(project, configuration, platform, ct).ConfigureAwait(false);
        var sessionHint = string.IsNullOrWhiteSpace(session)
            ? Path.GetFileNameWithoutExtension(targetPath)
            : session;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        System.Diagnostics.Process launched;
        try
        {
            KillSessionLocked(sessionHint!);
            launched = AppLauncher.Start(targetPath, startMinimized: !foreground);
        }
        finally { _gate.Release(); }

        try
        {
            var info = await WaitForDiscoveryAsync(
                launched, launched.Id, DiscoveryTimeout, ct).ConfigureAwait(false);
            var sessionName = ResolveSessionName(session, info.ProcessName);
            var source = LaunchSource.FromProject(project, configuration, platform, targetPath, foreground);
            var snapshot = await ConnectSessionAsync(
                sessionName, info, launched, source, ct).ConfigureAwait(false);
            if (foreground)
                await SendAsync(ToolCatalog.BringToFront, new { }, sessionName, ct).ConfigureAwait(false);
            return snapshot;
        }
        catch
        {
            AppLauncher.KillTree(launched);
            throw;
        }
    }

    /// <summary>
    /// Launch a prebuilt <c>.exe</c>/<c>.dll</c> with pilot enabled, wait for discovery, and attach
    /// as a named session. Does not rebuild.
    /// </summary>
    public async Task<SessionSnapshot> StartAppAsync(
        string path,
        string? session = null,
        string? workingDirectory = null,
        bool useStartupHook = true,
        string? uiFramework = null,
        bool foreground = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "App path is required.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new PilotCliException(
                PilotErrorCodes.NotFound,
                $"App path not found: {path}",
                "Pass a path to a pilot-enabled .exe or .dll.");

        var sessionHint = string.IsNullOrWhiteSpace(session)
            ? Path.GetFileNameWithoutExtension(fullPath)
            : session;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        System.Diagnostics.Process launched;
        try
        {
            KillSessionLocked(sessionHint!);
            try
            {
                launched = AppLauncher.Start(fullPath, workingDirectory, useStartupHook, uiFramework, !foreground);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                throw new PilotCliException(PilotErrorCodes.InvalidArgs, ex.Message, innerException: ex);
            }
        }
        finally { _gate.Release(); }

        try
        {
            var info = await WaitForDiscoveryAsync(
                launched, launched.Id, DiscoveryTimeout, ct).ConfigureAwait(false);
            var sessionName = ResolveSessionName(session, info.ProcessName);
            var source = LaunchSource.FromExe(fullPath, workingDirectory, useStartupHook, uiFramework, foreground);
            var snapshot = await ConnectSessionAsync(
                sessionName, info, launched, source, ct).ConfigureAwait(false);
            if (foreground)
                await SendAsync(ToolCatalog.BringToFront, new { }, sessionName, ct).ConfigureAwait(false);
            return snapshot;
        }
        catch
        {
            AppLauncher.KillTree(launched);
            throw;
        }
    }

    /// <summary>
    /// Launch a non-pilot process and track it as a named session (no discovery / MCP pipe).
    /// Does not replace the sticky active pilot session when one already exists.
    /// </summary>
    public async Task<SessionSnapshot> StartProcessAsync(
        string path,
        string? session = null,
        string? workingDirectory = null,
        string? arguments = null,
        bool showWindow = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PilotCliException(PilotErrorCodes.InvalidArgs, "Process path is required.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new PilotCliException(
                PilotErrorCodes.NotFound,
                $"Process path not found: {path}",
                "Pass a path to an .exe or .dll.");

        var sessionName = ResolveSessionName(session, Path.GetFileNameWithoutExtension(fullPath));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            KillSessionLocked(sessionName);

            System.Diagnostics.Process launched;
            try
            {
                launched = AppLauncher.StartProcess(fullPath, workingDirectory, arguments, showWindow);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
            {
                throw new PilotCliException(PilotErrorCodes.InvalidArgs, ex.Message, innerException: ex);
            }

            if (launched.HasExited)
            {
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"Process '{sessionName}' exited immediately (pid {launched.Id}).",
                    "Check the executable path, working directory, and arguments.");
            }

            var source = LaunchSource.FromProcess(fullPath, workingDirectory, arguments, showWindow);
            _sessions[sessionName] = new AppSession
            {
                Name = sessionName,
                Kind = SessionKind.Process,
                AttachedPid = launched.Id,
                Launched = launched,
                LaunchSource = source,
                ProcessNameHint = Path.GetFileNameWithoutExtension(fullPath),
            };

            // Process sessions are tracked but should not steal sticky pilot focus.
            if (_activeSession is null ||
                !_sessions.TryGetValue(_activeSession, out var active) ||
                active.Kind != SessionKind.Pilot ||
                active.Client is not { IsConnected: true })
            {
                // Prefer keeping an existing connected pilot session as active.
                var pilot = _sessions.Values.FirstOrDefault(s =>
                    s.Kind == SessionKind.Pilot && s.Client is { IsConnected: true });
                _activeSession = pilot?.Name ?? _activeSession;
            }

            return ToSnapshot(_sessions[sessionName], isActive: false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Poll a log file (or newest glob match) until <paramref name="pattern"/> matches.
    /// App-agnostic readiness helper — callers supply path and regex.
    /// </summary>
    public Task<LogWaitResult> WaitForLogAsync(
        string pathOrGlob,
        string pattern,
        int timeoutMs = 60_000,
        int pollMs = 200,
        bool fromEnd = false,
        CancellationToken ct = default) =>
        LogWaiter.WaitAsync(pathOrGlob, pattern, timeoutMs, pollMs, fromEnd, ct);

    public async Task<SessionSnapshot> RestartAsync(string? session = null, CancellationToken ct = default)
    {
        LaunchSource source;
        string sessionName;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var target = ResolveSessionForLifecycleLocked(session);
            if (target.LaunchSource is null)
            {
                throw new PilotCliException(
                    PilotErrorCodes.InvalidArgs,
                    $"Session '{target.Name}' cannot be restarted because it was attached, not launched by the CLI.",
                    "Use start_app or build_and_start for that session first.");
            }

            sessionName = target.Name;
            source = target.LaunchSource;
            KillSessionLocked(sessionName);
        }
        finally { _gate.Release(); }

        if (source.Kind == LaunchKind.Project)
            return await BuildAndStartAsync(
                source.Project!, source.Configuration!, source.Platform, sessionName, source.Foreground, ct).ConfigureAwait(false);
        if (source.Kind == LaunchKind.Process)
            return await StartProcessAsync(
                source.ExePath!, sessionName, source.WorkingDirectory, source.Arguments, source.ShowWindow, ct).ConfigureAwait(false);

        return await StartAppAsync(
            source.ExePath!,
            sessionName,
            source.WorkingDirectory,
            source.UseStartupHook,
            source.UiFramework,
            source.Foreground,
            ct).ConfigureAwait(false);
    }

    public SessionSnapshot? StopApp(string? session = null)
    {
        _gate.Wait();
        try
        {
            PruneExitedSessionsLocked();
            if (string.IsNullOrWhiteSpace(session) && !_sessions.Values.Any(s => !s.Exited))
                return null;

            var target = ResolveSessionForLifecycleLocked(session);
            var snapshot = ToSnapshot(target, isActive: false);
            KillSessionLocked(target.Name);
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<SessionSnapshot> StopAll()
    {
        _gate.Wait();
        try
        {
            var stopped = _sessions.Values
                .Select(s => ToSnapshot(s, isActive: false))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in _sessions.Keys.ToList())
                KillSessionLocked(name);

            return stopped;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Drops pipe attachment for one session (or the active/only session) without killing the process.
    /// Launch metadata is retained when the process was started by this CLI so restart_app still works.
    /// </summary>
    public SessionSnapshot? Detach(string? session = null)
    {
        _gate.Wait();
        try
        {
            PruneExitedSessionsLocked();
            if (string.IsNullOrWhiteSpace(session) && !_sessions.Values.Any(s => !s.Exited))
                return null;

            var target = ResolveSessionForLifecycleLocked(session);
            var snapshot = ToSnapshot(target, isActive: false);

            if (target.Kind == SessionKind.Process)
            {
                // Forget tracking; leave the OS process running.
                if (target.Launched is not null)
                {
                    try { target.Launched.Dispose(); } catch { /* ignore */ }
                    target.Launched = null;
                }
                RemoveSessionLocked(target.Name);
                return snapshot;
            }

            // Keep launch metadata for restart; drop only the live pipe.
            if (target.LaunchSource is not null)
            {
                ResetSessionConnectionLocked(target);
                target.Info = null;
                if (string.Equals(_activeSession, target.Name, StringComparison.OrdinalIgnoreCase))
                    _activeSession = _sessions.Keys.FirstOrDefault(k =>
                        !string.Equals(k, target.Name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                RemoveSessionLocked(target.Name);
            }

            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            foreach (var name in _sessions.Keys.ToList())
                ResetSessionConnectionLocked(_sessions[name]);
            _sessions.Clear();
            _activeSession = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<SessionSnapshot> ConnectSessionAsync(
        string sessionName,
        DiscoveryInfo info,
        System.Diagnostics.Process? launched,
        LaunchSource? launchSource,
        CancellationToken ct)
    {
        // Pipe connect, auth, and MCP initialize may each wait. None may hold the global
        // session dictionary gate, otherwise unrelated sessions and status polls stall.
        var client = await McpPipeClient.ConnectAsync(
            info.PipeName, info.Token, PipeConnectTimeoutMs, ct).ConfigureAwait(false);

        var gateHeld = false;
        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            gateHeld = true;
            if (_sessions.TryGetValue(sessionName, out var existing))
            {
                // Replacing an existing session name: drop old pipe; kill prior CLI-launched process if different.
                // Never kill attach-only PIDs — those processes were not started by this CLI.
                if (existing.Launched is not null && existing.Launched.Id != info.Pid)
                    AppLauncher.KillTree(existing.Launched);
                ResetSessionConnectionLocked(existing);
                _sessions.Remove(sessionName);
            }

            var connected = new AppSession
            {
                Name = sessionName,
                Kind = SessionKind.Pilot,
                Client = client,
                Info = info,
                AttachedPid = info.Pid,
                Launched = launched,
                LaunchSource = launchSource,
                ProcessNameHint = info.ProcessName,
            };
            _sessions[sessionName] = connected;
            _activeSession = sessionName;
            return ToSnapshot(connected, isActive: true);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        finally
        {
            if (gateHeld)
                _gate.Release();
        }
    }

    private AppSession ResolveSessionForSendLocked(string? session)
    {
        if (!string.IsNullOrWhiteSpace(session))
        {
            var named = RequireSessionLocked(session);
            if (named.Kind != SessionKind.Pilot || named.Client is not { IsConnected: true })
            {
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"Session '{named.Name}' is not a connected pilot app.",
                    named.Kind == SessionKind.Process
                        ? "Process sessions have no UI pipe. Use wait_for_log / stop_app, or attach/start_app for a pilot UI."
                        : "Use attach, start_app, or build_and_start for that session.");
            }
            return named;
        }

        if (!string.IsNullOrWhiteSpace(_activeSession) &&
            _sessions.TryGetValue(_activeSession, out var active) &&
            active.Kind == SessionKind.Pilot &&
            active.Client is { IsConnected: true })
        {
            return active;
        }

        var connected = _sessions.Values
            .Where(s => s.Kind == SessionKind.Pilot && s.Client is { IsConnected: true })
            .ToList();
        if (connected.Count == 1)
            return connected[0];
        if (connected.Count == 0)
        {
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                "No app is attached.",
                "Use attach with an explicit target, start_app, or build_and_start before calling app tools.");
        }

        throw new PilotCliException(
            PilotErrorCodes.Ambiguous,
            $"Multiple sessions are attached ({connected.Count}). Pass session or call select_session first.",
            "Use list_sessions, then pass session on the tool or call select_session.");
    }

    private AppSession ResolveSessionForLifecycleLocked(string? session)
    {
        if (!string.IsNullOrWhiteSpace(session))
            return RequireSessionLocked(session);

        if (!string.IsNullOrWhiteSpace(_activeSession) &&
            _sessions.TryGetValue(_activeSession, out var active) &&
            !active.Exited)
            return active;

        var live = _sessions.Values.Where(candidate => !candidate.Exited).ToList();
        if (live.Count == 1)
            return live[0];

        if (live.Count == 0)
        {
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                "No live app session exists.",
                "Use attach, start_app, or build_and_start first.");
        }

        throw new PilotCliException(
            PilotErrorCodes.Ambiguous,
            $"Multiple live sessions are attached ({live.Count}). Pass session or call select_session first.",
            "Use list_sessions, then pass an explicit session name.");
    }

    private AppSession RequireSessionLocked(string session)
    {
        if (_sessions.TryGetValue(session, out var found))
            return found;

        throw new PilotCliException(
            PilotErrorCodes.NotFound,
            $"No session named '{session}'.",
            "Use list_sessions to see attached sessions.");
    }

    private void KillSessionLocked(string sessionName)
    {
        if (!_sessions.TryGetValue(sessionName, out var session))
            return;

        var attachedPid = session.AttachedPid ?? session.Info?.Pid;
        ResetSessionConnectionLocked(session);
        AppLauncher.KillTree(session.Launched);
        if (attachedPid.HasValue)
            AppLauncher.KillByPid(attachedPid.Value);
        RemoveSessionLocked(sessionName);
    }

    /// <summary>
    /// Pulls a session's window to the foreground. Call sites already hold <see cref="_gate"/>, so
    /// this talks to the pipe directly instead of going through <see cref="SendAsync"/>.
    /// </summary>
    private async Task BringToFrontLocked(string sessionName, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionName, out var session) ||
            session.Client is not { IsConnected: true })
        {
            return;
        }

        try { await session.Client.CallToolAsync(ToolCatalog.BringToFront, new { }, ct).ConfigureAwait(false); }
        catch { /* showing the window must never fail the launch */ }
    }

    private void RemoveSessionLocked(string sessionName)
    {
        _sessions.Remove(sessionName);
        if (string.Equals(_activeSession, sessionName, StringComparison.OrdinalIgnoreCase))
            _activeSession = _sessions.Keys.FirstOrDefault();
    }

    private void PruneExitedSessionsLocked(IReadOnlySet<int>? alivePids = null)
    {
        foreach (var session in _sessions.Values.ToList())
        {
            var exited = session.Launched is not null
                ? HasExited(session.Launched)
                : session.AttachedPid is int pid &&
                  (alivePids != null
                      ? !alivePids.Contains(pid)
                      : _discovery.FindByPid(pid) is null);
            if (!exited)
                continue;

            ResetSessionConnectionLocked(session);
            try { session.Launched?.Dispose(); } catch { /* ignore */ }
            session.Launched = null;
            session.Info = null;
            session.AttachedPid = null;

            if (session.LaunchSource is null)
            {
                RemoveSessionLocked(session.Name);
                continue;
            }

            // Keep only the launch recipe so restart_app can recover a crashed or manually
            // closed app. Exited sessions are hidden from ListSessions and status snapshots.
            session.Exited = true;
            if (string.Equals(_activeSession, session.Name, StringComparison.OrdinalIgnoreCase))
            {
                _activeSession = _sessions.Values
                    .FirstOrDefault(candidate => !candidate.Exited && candidate.Client is { IsConnected: true })
                    ?.Name;
            }
        }
    }

    private static bool HasExited(System.Diagnostics.Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    private static void ResetSessionConnectionLocked(AppSession session)
    {
        try { session.Client?.Dispose(); } catch { /* ignore */ }
        session.Client = null;
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
                "Use list_apps to see currently discoverable apps, or start_app / build_and_start to launch one.");
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
            return "No running UiPilot apps found. Use start_app / build_and_start or launch one, then attach.";

        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(processName))
            filters.Add($"processName contains '{processName}'");
        if (!string.IsNullOrWhiteSpace(uiFramework))
            filters.Add($"uiFramework equals '{uiFramework}'");
        return "No running UiPilot apps matched " + string.Join(" and ", filters) + ".";
    }

    private async Task<DiscoveryInfo> WaitForDiscoveryAsync(
        System.Diagnostics.Process launched,
        int pid,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var pollMs = DiscoveryInitialPollMs;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var info = _discovery.FindByPid(pid);
            if (info != null) return info;
            if (launched.HasExited)
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"App process {pid} exited before publishing a discovery file.",
                    "Check the app startup failure (DOTNET_STARTUP_HOOKS / PilotHost.Start), then run start_app / build_and_start again.");
            await Task.Delay(pollMs, ct).ConfigureAwait(false);
            pollMs = Math.Min(pollMs * 2, DiscoveryMaxPollMs);
        }
        throw new TimeoutException($"Timed out waiting for app {pid} to publish its discovery file.");
    }

    private static string ResolveSessionName(string? session, string? processName)
    {
        if (!string.IsNullOrWhiteSpace(session))
            return session.Trim();
        if (!string.IsNullOrWhiteSpace(processName))
            return processName.Trim();
        return "default";
    }

    private static SessionSnapshot ToSnapshot(AppSession session, bool isActive) => new()
    {
        Name = session.Name,
        Kind = session.Kind == SessionKind.Process ? "process" : "pilot",
        IsActive = isActive,
        Pid = session.Info?.Pid ?? session.AttachedPid ?? session.Launched?.Id ?? 0,
        ProcessName = session.Info?.ProcessName
            ?? session.ProcessNameHint
            ?? Path.GetFileNameWithoutExtension(session.LaunchSource?.ExePath)
            ?? session.Name,
        MainWindowTitle = session.Info?.MainWindowTitle,
        UiFramework = session.Info?.UiFramework,
        LaunchedByCli = session.LaunchSource is not null,
        CanRestart = session.LaunchSource is not null,
    };

    internal static JsonElement WrapWithSession(JsonElement result, string sessionName)
    {
        if (result.ValueKind == JsonValueKind.Object)
        {
            var node = JsonNode.Parse(result.GetRawText())?.AsObject() ?? new JsonObject();
            node["session"] = sessionName;
            return JsonSerializer.SerializeToElement(node);
        }

        if (result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return JsonSerializer.SerializeToElement(new { session = sessionName, result = (object?)null });

        return JsonSerializer.SerializeToElement(new { session = sessionName, result });
    }

    private enum SessionKind { Pilot, Process }

    private enum LaunchKind { Project, Exe, Process }

    private sealed class LaunchSource
    {
        public LaunchKind Kind { get; private init; }
        public string? Project { get; private init; }
        public string? Configuration { get; private init; }
        public string? Platform { get; private init; }
        public string? ExePath { get; private init; }
        public string? WorkingDirectory { get; private init; }
        public string? Arguments { get; private init; }
        public bool UseStartupHook { get; private init; } = true;
        public string? UiFramework { get; private init; }
        public bool Foreground { get; private init; }
        public bool ShowWindow { get; private init; } = true;

        public static LaunchSource FromProject(
            string project,
            string configuration,
            string? platform,
            string targetPath,
            bool foreground = false) => new()
        {
            Kind = LaunchKind.Project,
            Project = project,
            Configuration = configuration,
            Platform = platform,
            ExePath = targetPath,
            UseStartupHook = true,
            Foreground = foreground,
        };

        public static LaunchSource FromExe(
            string exePath,
            string? workingDirectory,
            bool useStartupHook = true,
            string? uiFramework = null,
            bool foreground = false) => new()
        {
            Kind = LaunchKind.Exe,
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            UseStartupHook = useStartupHook,
            UiFramework = uiFramework,
            Foreground = foreground,
        };

        public static LaunchSource FromProcess(
            string exePath,
            string? workingDirectory,
            string? arguments,
            bool showWindow = true) => new()
        {
            Kind = LaunchKind.Process,
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            UseStartupHook = false,
            ShowWindow = showWindow,
        };
    }

    private sealed class AppSession
    {
        public required string Name { get; init; }
        public SessionKind Kind { get; init; } = SessionKind.Pilot;
        public McpPipeClient? Client { get; set; }
        public DiscoveryInfo? Info { get; set; }
        public int? AttachedPid { get; set; }
        public System.Diagnostics.Process? Launched { get; set; }
        public LaunchSource? LaunchSource { get; set; }
        public string? ProcessNameHint { get; set; }
        public bool Exited { get; set; }
    }
}

internal sealed record ConnectionStateSnapshot(
    string? ActiveSession,
    IReadOnlyList<SessionSnapshot> Sessions,
    IReadOnlyList<DiscoveryInfo> Apps);
