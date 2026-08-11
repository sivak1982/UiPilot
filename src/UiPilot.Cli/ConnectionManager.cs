using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiPilot.Cli.Discovery;
using UiPilot.Cli.Pipe;
using UiPilot.Cli.Process;
using UiPilot.Server;
using UiPilot.Tools;

namespace UiPilot.Cli;

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
/// apps (e.g. Simulation + Operator Interface) at once.
/// </summary>
public sealed class ConnectionManager : IDisposable
{
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
            try { return _activeSession; }
            finally { _gate.Release(); }
        }
    }

    public IReadOnlyList<SessionSnapshot> ListSessions()
    {
        _gate.Wait();
        try
        {
            return _sessions.Values
                .Select(s => ToSnapshot(s, string.Equals(s.Name, _activeSession, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var info = ResolveTarget(pid, processName, uiFramework);
            var sessionName = ResolveSessionName(session, info.ProcessName);
            await ConnectSessionLocked(sessionName, info, launched: null, launchSource: null, ct).ConfigureAwait(false);
            return ToSnapshot(_sessions[sessionName], isActive: true);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonElement> SendAsync(string method, object? args, string? session = null, CancellationToken ct = default)
    {
        AppSession target;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            target = ResolveSessionForSendLocked(session);
            if (target.Client is not { IsConnected: true })
            {
                ResetSessionConnectionLocked(target);
                throw new PilotCliException(
                    PilotErrorCodes.NotAttached,
                    $"Session '{target.Name}' is not connected.",
                    "Use attach, start_app, or build_and_start for that session.");
            }
        }
        finally { _gate.Release(); }

        try
        {
            if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await target.Client.PingAsync(ct).ConfigureAwait(false);
                return JsonSerializer.SerializeToElement(new { pong = true, session = target.Name });
            }

            if (string.Equals(method, "describe", StringComparison.OrdinalIgnoreCase))
            {
                var described = await target.Client.ListToolsAsync(ct).ConfigureAwait(false);
                return WrapWithSession(described, target.Name);
            }

            var result = await target.Client.CallToolAsync(method, args, ct).ConfigureAwait(false);
            return WrapWithSession(result, target.Name);
        }
        catch (IOException)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try { ResetSessionConnectionLocked(target); }
            finally { _gate.Release(); }

            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                $"Lost connection to session '{target.Name}' (the app may have exited). Re-attach or restart.",
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

    public async Task<SessionSnapshot> BuildAndStartAsync(
        string project,
        string configuration,
        string? platform,
        string? session = null,
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
        try
        {
            KillSessionLocked(sessionHint!);

            var launched = AppLauncher.Start(targetPath);
            try
            {
                var info = await WaitForDiscoveryAsync(launched, launched.Id, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                var sessionName = ResolveSessionName(session, info.ProcessName);
                // If auto-name differs from the pre-kill hint, ensure we don't leave a stale empty slot.
                if (!string.Equals(sessionName, sessionHint, StringComparison.OrdinalIgnoreCase))
                    KillSessionLocked(sessionName);

                var source = LaunchSource.FromProject(project, configuration, platform, targetPath);
                await ConnectSessionLocked(sessionName, info, launched, source, ct).ConfigureAwait(false);
                return ToSnapshot(_sessions[sessionName], isActive: true);
            }
            catch
            {
                AppLauncher.KillTree(launched);
                throw;
            }
        }
        finally { _gate.Release(); }
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
        try
        {
            KillSessionLocked(sessionHint!);

            System.Diagnostics.Process launched;
            try
            {
                launched = AppLauncher.Start(fullPath, workingDirectory, useStartupHook, uiFramework);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
            {
                throw new PilotCliException(PilotErrorCodes.InvalidArgs, ex.Message, innerException: ex);
            }

            try
            {
                var info = await WaitForDiscoveryAsync(launched, launched.Id, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                var sessionName = ResolveSessionName(session, info.ProcessName);
                if (!string.Equals(sessionName, sessionHint, StringComparison.OrdinalIgnoreCase))
                    KillSessionLocked(sessionName);

                var source = LaunchSource.FromExe(fullPath, workingDirectory, useStartupHook, uiFramework);
                await ConnectSessionLocked(sessionName, info, launched, source, ct).ConfigureAwait(false);
                return ToSnapshot(_sessions[sessionName], isActive: true);
            }
            catch
            {
                AppLauncher.KillTree(launched);
                throw;
            }
        }
        finally { _gate.Release(); }
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
                launched = AppLauncher.StartProcess(fullPath, workingDirectory, arguments);
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

            var source = LaunchSource.FromProcess(fullPath, workingDirectory, arguments);
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
            return await BuildAndStartAsync(source.Project!, source.Configuration!, source.Platform, sessionName, ct).ConfigureAwait(false);
        if (source.Kind == LaunchKind.Process)
            return await StartProcessAsync(source.ExePath!, sessionName, source.WorkingDirectory, source.Arguments, ct).ConfigureAwait(false);

        return await StartAppAsync(
            source.ExePath!,
            sessionName,
            source.WorkingDirectory,
            source.UseStartupHook,
            source.UiFramework,
            ct).ConfigureAwait(false);
    }

    public SessionSnapshot? StopApp(string? session = null)
    {
        _gate.Wait();
        try
        {
            if (string.IsNullOrWhiteSpace(session) && _sessions.Count > 1 && _activeSession is null)
            {
                throw new PilotCliException(
                    PilotErrorCodes.Ambiguous,
                    $"Multiple sessions are attached ({_sessions.Count}). Pass session or call select_session first.",
                    "Use list_sessions, then stop_app with an explicit session name (or stop_all).");
            }

            if (_sessions.Count == 0)
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
            if (_sessions.Count == 0)
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

    private async Task ConnectSessionLocked(
        string sessionName,
        DiscoveryInfo info,
        System.Diagnostics.Process? launched,
        LaunchSource? launchSource,
        CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionName, out var existing))
        {
            // Replacing an existing session name: drop old pipe; kill prior CLI-launched process if different.
            if (existing.Launched is not null && existing.Launched.Id != info.Pid)
                AppLauncher.KillTree(existing.Launched);
            else if (existing.AttachedPid is int oldPid && oldPid != info.Pid && existing.Launched is null)
                AppLauncher.KillByPid(oldPid);
            ResetSessionConnectionLocked(existing);
            _sessions.Remove(sessionName);
        }

        var client = await McpPipeClient.ConnectAsync(info.PipeName, info.Token, 5000, ct).ConfigureAwait(false);
        _sessions[sessionName] = new AppSession
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
        _activeSession = sessionName;
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

        if (!string.IsNullOrWhiteSpace(_activeSession) && _sessions.TryGetValue(_activeSession, out var active))
            return active;

        if (_sessions.Count == 1)
            return _sessions.Values.First();

        if (_sessions.Count == 0)
        {
            throw new PilotCliException(
                PilotErrorCodes.NotAttached,
                "No app session exists.",
                "Use attach, start_app, or build_and_start first.");
        }

        throw new PilotCliException(
            PilotErrorCodes.Ambiguous,
            $"Multiple sessions are attached ({_sessions.Count}). Pass session or call select_session first.",
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

    private void RemoveSessionLocked(string sessionName)
    {
        _sessions.Remove(sessionName);
        if (string.Equals(_activeSession, sessionName, StringComparison.OrdinalIgnoreCase))
            _activeSession = _sessions.Keys.FirstOrDefault();
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
            await Task.Delay(200, ct).ConfigureAwait(false);
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

    private static JsonElement WrapWithSession(JsonElement result, string sessionName)
    {
        if (result.ValueKind == JsonValueKind.Object)
        {
            var node = JsonNode.Parse(result.GetRawText())!.AsObject();
            node["session"] = sessionName;
            return JsonSerializer.SerializeToElement(node);
        }

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

        public static LaunchSource FromProject(string project, string configuration, string? platform, string targetPath) => new()
        {
            Kind = LaunchKind.Project,
            Project = project,
            Configuration = configuration,
            Platform = platform,
            ExePath = Path.ChangeExtension(targetPath, ".exe"),
            UseStartupHook = true,
        };

        public static LaunchSource FromExe(
            string exePath,
            string? workingDirectory,
            bool useStartupHook = true,
            string? uiFramework = null) => new()
        {
            Kind = LaunchKind.Exe,
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            UseStartupHook = useStartupHook,
            UiFramework = uiFramework,
        };

        public static LaunchSource FromProcess(string exePath, string? workingDirectory, string? arguments) => new()
        {
            Kind = LaunchKind.Process,
            ExePath = exePath,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            UseStartupHook = false,
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
    }
}
