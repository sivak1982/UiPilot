using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using UiPilot.Abstraction;
using UiPilot.Hosting;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf;

/// <summary>
/// The single public entry point for WPF apps. Call <see cref="Start()"/> once from app startup.
/// No DI, no Generic Host, no attributes required. Enabled only for Debug builds, when the
/// <c>UIPILOT_ENABLE=1</c> environment variable is set, or when
/// <c>Start(force: true)</c> is used.
/// </summary>
public static class PilotHost
{
    public const string ProtocolVersion = PilotRuntime.ProtocolVersion;

    private static readonly object Gate = new object();
    private static readonly PilotRuntime Runtime = new PilotRuntime();
    private static bool _hooksAttached;
    private static bool _starting;

    /// <summary>True once the automation surface is live.</summary>
    public static bool IsRunning => Runtime.IsRunning;

    /// <summary>The registry of tools, available after a successful <see cref="Start()"/> (else null).</summary>
    public static ToolRegistry? Tools => Runtime.Tools;

    /// <summary>Start with default options.</summary>
    public static void Start() => Start((WpfOptions?)null);

    /// <summary>Start, forcing enablement even in Release builds.</summary>
    public static void Start(bool force) => Start(new WpfOptions { Force = force });

    /// <summary>Start with explicit options. Idempotent and safe to call more than once.</summary>
    public static void Start(WpfOptions? options)
    {
        options ??= new WpfOptions();
        lock (Gate)
        {
            if (Runtime.IsRunning || _starting)
                return;
            _starting = true;
        }

        WpfUiBackend? backend = null;
        try
        {
            var app = Application.Current;
            if (app == null)
            {
                Log("UiPilot cannot start: no WPF Application.Current. Call Start() after the app is created.");
                return;
            }

            var dispatcher = app.Dispatcher;
            backend = new WpfUiBackend();
            dispatcher.Invoke(() => backend.Install());

            Func<Func<object?>, object?> invoke = func => dispatcher.Invoke(func);

            var pilotOptions = options.ToPilotOptions();
            var started = Runtime.Start(
                pilotOptions,
                backend,
                invoke,
                () => app.MainWindow?.Title,
                Log);

            if (!started)
            {
                try { backend.Shutdown(); } catch { /* ignore */ }
                backend = null;
                return;
            }

            lock (Gate)
            {
                if (!_hooksAttached)
                {
                    _hooksAttached = true;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                    dispatcher.ShutdownStarted += (_, _) => Stop();
                }
            }

            if (PilotRuntime.ResolveStartMinimized(pilotOptions))
                ScheduleMinimize(app, dispatcher);
        }
        catch
        {
            if (!Runtime.IsRunning)
                try { backend?.Shutdown(); } catch { /* ignore */ }
            throw;
        }
        finally
        {
            lock (Gate) _starting = false;
        }
    }

    /// <summary>Tear down the server and remove the discovery file. Safe to call repeatedly.</summary>
    public static void Stop() => Runtime.Stop(Log);

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

    private static void Log(string message) => Debug.WriteLine("[UiPilot] " + message);
}
