using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using WpfPilot.Inspection;
using WpfPilot.Server;
using WpfPilot.Tools;

namespace WpfPilot;

/// <summary>
/// The single public entry point. Call <see cref="Start()"/> once from your app startup.
/// No DI, no Generic Host, no attributes required. Enabled only for Debug builds, when the
/// <c>WPFPILOT_ENABLE=1</c> environment variable is set, or when <c>Start(force: true)</c> is
/// used - so a shipped Release build never exposes an automation surface by accident.
/// </summary>
public static class WpfPilotHost
{
    public const string ProtocolVersion = "1.0";

    private static readonly object Gate = new object();
    private static bool _started;
    private static NamedPipeServer? _server;
    private static BindingDiagnostics? _bindings;
    private static string? _discoveryPath;

    /// <summary>True once the automation surface is live.</summary>
    public static bool IsRunning
    {
        get { lock (Gate) return _started; }
    }

    /// <summary>The registry of tools, available after a successful <see cref="Start()"/> (else null).</summary>
    public static ToolRegistry? Tools { get; private set; }

    /// <summary>Start with default options.</summary>
    public static void Start() => Start((WpfPilotOptions?)null);

    /// <summary>Start, forcing enablement even in Release builds.</summary>
    public static void Start(bool force) => Start(new WpfPilotOptions { Force = force });

    /// <summary>Start with explicit options. Idempotent and safe to call more than once.</summary>
    public static void Start(WpfPilotOptions? options)
    {
        options ??= new WpfPilotOptions();
        lock (Gate)
        {
            if (_started) return;

            if (!IsEnabled(options))
            {
                Log("WpfPilot disabled (not a Debug build, WPFPILOT_ENABLE!=1, and Force=false).");
                return;
            }

            var app = Application.Current;
            if (app == null)
            {
                Log("WpfPilot cannot start: no WPF Application.Current. Call Start() after the app is created.");
                return;
            }

            var dispatcher = app.Dispatcher;
            var elements = new ElementRegistry();
            _bindings = new BindingDiagnostics();
            dispatcher.Invoke(() => _bindings.Install());

            var context = new ToolContext(dispatcher, elements, _bindings);
            var registry = new ToolRegistry(context);
            BuiltInTools.RegisterAll(registry);
            Tools = registry;

            var pid = Process.GetCurrentProcess().Id;
            var token = string.IsNullOrEmpty(options.Token) ? GenerateToken() : options.Token!;
            var pipeName = string.IsNullOrEmpty(options.PipeName)
                ? $"wpfpilot.{pid}.{Guid.NewGuid():N}"
                : options.PipeName!;

            _server = new NamedPipeServer(pipeName, token, registry, Log);
            _server.Start();

            var info = new DiscoveryInfo
            {
                Pid = pid,
                ProcessName = SafeProcessName(),
                PipeName = pipeName,
                Token = token,
                ProtocolVersion = ProtocolVersion,
                StartedUtc = DateTime.UtcNow.ToString("o"),
                MainWindowTitle = dispatcher.Invoke(() => app.MainWindow?.Title),
            };
            _discoveryPath = DiscoveryFile.Write(info, options.DiscoveryDirectory);

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
            dispatcher.ShutdownStarted += (_, _) => Stop();

            _started = true;
            Log($"WpfPilot started. pipe={pipeName} discovery={_discoveryPath}");

            if (ResolveStartMinimized(options))
                ScheduleMinimize(app, dispatcher);
        }
    }

    private static bool ResolveStartMinimized(WpfPilotOptions options)
    {
        if (options.StartMinimized.HasValue) return options.StartMinimized.Value;
        return string.Equals(
            Environment.GetEnvironmentVariable(WpfPilotOptions.StartMinimizedEnvVar), "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Minimize the main window once, at idle priority, so it has rendered at least one frame first
    /// (keeping offscreen screenshots valid). Never throws into the host app.
    /// </summary>
    private static void ScheduleMinimize(Application app, Dispatcher dispatcher)
    {
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var window = app.MainWindow;
                if (window != null) window.WindowState = WindowState.Minimized;
            }
            catch { /* ignore - minimizing is best-effort */ }
        }));
    }

    /// <summary>Tear down the server and remove the discovery file. Safe to call repeatedly.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            if (!_started) return;
            try { _server?.Stop(); } catch { /* ignore */ }
            try { _bindings?.Uninstall(); } catch { /* ignore */ }
            if (_discoveryPath != null) DiscoveryFile.Delete(_discoveryPath);
            _server = null;
            _bindings = null;
            _discoveryPath = null;
            Tools = null;
            _started = false;
            Log("WpfPilot stopped.");
        }
    }

    private static bool IsEnabled(WpfPilotOptions options)
    {
        if (options.Force) return true;
        if (string.Equals(Environment.GetEnvironmentVariable(WpfPilotOptions.EnableEnvVar), "1", StringComparison.Ordinal))
            return true;
        return IsEntryAssemblyDebugBuild();
    }

    private static bool IsEntryAssemblyDebugBuild()
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

    private static void Log(string message) => Debug.WriteLine("[WpfPilot] " + message);
}
