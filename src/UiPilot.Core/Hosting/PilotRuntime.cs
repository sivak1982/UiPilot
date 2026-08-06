using System;
using System.Diagnostics;
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
    public const string ProtocolVersion = "1.2";

    private readonly object _gate = new object();
    private NamedPipeServer? _server;
    private IUiBackend? _backend;
    private string? _discoveryPath;
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

            _backend = backend;
            var context = new ToolContext(backend, invokeOnUi);
            var registry = new ToolRegistry(context);
            BuiltInTools.RegisterAll(registry);
            Tools = registry;

            var pid = Process.GetCurrentProcess().Id;
            var token = string.IsNullOrEmpty(options.Token) ? GenerateToken() : options.Token!;
            var pipeName = string.IsNullOrEmpty(options.PipeName)
                ? $"uipilot.{pid}.{Guid.NewGuid():N}"
                : options.PipeName!;

            _server = new NamedPipeServer(pipeName, token, registry, log);
            _server.Start();

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
            };
            _discoveryPath = DiscoveryFile.Write(info, options.DiscoveryDirectory);

            _started = true;
            log($"Pilot started ({info.UiFramework}). pipe={pipeName} discovery={_discoveryPath}");
            return true;
        }
    }

    public void Stop(Action<string>? log = null)
    {
        lock (_gate)
        {
            if (!_started) return;
            try { _server?.Stop(); } catch { /* ignore */ }
            try { _backend?.Shutdown(); } catch { /* ignore */ }
            if (_discoveryPath != null) DiscoveryFile.Delete(_discoveryPath);
            _server = null;
            _backend = null;
            _discoveryPath = null;
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

    private static string SafeProcessName()
    {
        try { return Process.GetCurrentProcess().ProcessName; }
        catch { return "unknown"; }
    }
}
