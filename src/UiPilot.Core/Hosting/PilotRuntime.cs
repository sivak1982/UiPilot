using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UiPilot.Abstraction;
using UiPilot.Server;
using UiPilot.Tools;

namespace UiPilot.Hosting;

/// <summary>
/// Shared start/stop wiring for any UI framework backend: tool registry, named pipe, discovery file.
/// Framework hosts (<c>UiPilot.Wpf.PilotHost</c>, <c>UiPilot.Avalonia.PilotHost</c>) stay thin wrappers around this.
/// </summary>
public sealed class PilotRuntime : IDisposable
{
    public const string ProtocolVersion = "2.0";

    private readonly object _gate = new object();
    private McpPipeServer? _server;
    private IUiBackend? _backend;
    private string? _discoveryPath;
    private FileStream? _processLock;
    private bool _started;

    public bool IsRunning
    {
        get { lock (_gate) return _started; }
    }

    public ToolRegistry? Tools { get; private set; }

    /// <summary>
    /// Start the automation surface. Returns false when enablement gates say no.
    /// Idempotent.
    /// </summary>
    public bool Start(
        PilotOptions options,
        IUiBackend backend,
        Func<Func<object?>, object?> invokeOnUi,
        Func<string?> getMainWindowTitle,
        Action<string> log)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (backend == null) throw new ArgumentNullException(nameof(backend));
        if (invokeOnUi == null) throw new ArgumentNullException(nameof(invokeOnUi));

        lock (_gate)
        {
            if (_started) return true;

            if (!IsEnabled(options))
            {
                log("Pilot disabled (not a Debug build, UIPILOT_ENABLE!=1, and Force=false).");
                return false;
            }

            var context = new ToolContext(backend, invokeOnUi);
            var registry = new ToolRegistry(context);
            BuiltInTools.RegisterAll(registry);

            var pid = Process.GetCurrentProcess().Id;
            var processLock = DiscoveryFile.TryAcquireProcessLock(pid, options.DiscoveryDirectory);
            if (processLock == null)
            {
                log("Pilot is already running for this process and discovery directory.");
                return false;
            }
            var token = string.IsNullOrEmpty(options.Token) ? GenerateToken() : options.Token!;
            var pipeName = string.IsNullOrEmpty(options.PipeName)
                ? $"uipilot.{pid}.{Guid.NewGuid():N}"
                : options.PipeName!;

            McpPipeServer? server = null;
            string? discoveryPath = null;
            try
            {
                server = new McpPipeServer(pipeName, token, registry, log);
                server.Start();

                var info = new DiscoveryInfo
                {
                    Pid = pid,
                    ProcessName = SafeProcessName(),
                    PipeName = pipeName,
                    Token = token,
                    ProtocolVersion = ProtocolVersion,
                    StartedUtc = DateTime.UtcNow.ToString("o"),
                    MainWindowTitle = invokeOnUi(() => getMainWindowTitle()) as string,
                    UiFramework = string.IsNullOrEmpty(options.UiFramework) ? backend.Framework : options.UiFramework,
                    Capabilities = UiBackendCapabilities.Describe(backend),
                };
                discoveryPath = DiscoveryFile.Write(info, options.DiscoveryDirectory);

                _backend = backend;
                Tools = registry;
                _server = server;
                _discoveryPath = discoveryPath;
                _processLock = processLock;
                _started = true;
                log($"Pilot started ({info.UiFramework}). pipe={pipeName} discovery={_discoveryPath}");
                return true;
            }
            catch
            {
                try { server?.Stop(); } catch { /* rollback is best-effort */ }
                if (discoveryPath != null)
                    DiscoveryFile.Delete(discoveryPath);
                ReleaseProcessLock(processLock);
                _backend = null;
                Tools = null;
                _server = null;
                _discoveryPath = null;
                _started = false;
                throw;
            }
        }
    }

    public void Stop(Action<string>? log = null)
    {
        lock (_gate)
        {
            if (!_started) return;
            try { _server?.Stop(); } catch { /* ignore */ }
            // The server cancellation stops new work. Let handlers already using the backend
            // leave before adapter Shutdown tears down diagnostics and framework resources.
            if (Tools is { } tools && !tools.WaitForIdle(TimeSpan.FromSeconds(5)))
                log?.Invoke("Pilot stop timed out waiting for active tool calls.");
            try { _backend?.Shutdown(); } catch { /* ignore */ }
            if (_discoveryPath != null) DiscoveryFile.Delete(_discoveryPath);
            ReleaseProcessLock(_processLock);
            _server = null;
            _backend = null;
            _discoveryPath = null;
            _processLock = null;
            Tools = null;
            _started = false;
            log?.Invoke("Pilot stopped.");
        }
    }

    public void Dispose() => Stop();

    public static bool IsEnabled(PilotOptions options)
    {
        if (options.Force) return true;
        if (PilotOptions.IsEnableEnvSet()) return true;
        return IsEntryAssemblyDebugBuild();
    }

    public static bool ResolveStartMinimized(PilotOptions options)
    {
        if (options.StartMinimized.HasValue) return options.StartMinimized.Value;
        return PilotOptions.IsStartMinimizedEnvSet();
    }

    public static bool IsEntryAssemblyDebugBuild()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly();
            var attr = asm?.GetCustomAttribute<DebuggableAttribute>();
            return attr != null && attr.IsJITOptimizerDisabled;
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateToken() =>
        Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static void ReleaseProcessLock(FileStream? processLock)
    {
        if (processLock == null) return;
        try { processLock.Dispose(); } catch { /* ignore */ }
    }

    private static string SafeProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "unknown"; }
    }
}
