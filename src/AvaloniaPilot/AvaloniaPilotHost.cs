using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using WpfPilot;
using WpfPilot.Hosting;
using WpfPilot.Tools;

namespace AvaloniaPilot;

/// <summary>
/// Entry point for Avalonia apps. Call <see cref="Start()"/> once after the application is created
/// (typically from <c>App.OnFrameworkInitializationCompleted</c>). Same pipe/MCP protocol as WpfPilot.
/// </summary>
public static class AvaloniaPilotHost
{
    public const string ProtocolVersion = PilotRuntime.ProtocolVersion;

    private static readonly object Gate = new object();
    private static readonly PilotRuntime Runtime = new PilotRuntime();
    private static bool _hooksAttached;

    public static bool IsRunning => Runtime.IsRunning;

    public static ToolRegistry? Tools => Runtime.Tools;

    public static void Start() => Start((AvaloniaPilotOptions?)null);

    public static void Start(bool force) => Start(new AvaloniaPilotOptions { Force = force });

    public static void Start(AvaloniaPilotOptions? options)
    {
        options ??= new AvaloniaPilotOptions();
        lock (Gate)
        {
            if (Runtime.IsRunning)
                return;

            if (Application.Current == null)
            {
                Log("AvaloniaPilot cannot start: no Avalonia Application.Current.");
                return;
            }

            var backend = new AvaloniaUiBackend();
            InvokeOnUi(() => { backend.Install(); return null; });

            var pilotOptions = options.ToPilotOptions();
            var started = Runtime.Start(
                pilotOptions,
                backend,
                func => InvokeOnUi(func),
                () => GetMainWindow()?.Title,
                Log);

            if (!started)
            {
                try { backend.Shutdown(); } catch { /* ignore */ }
                return;
            }

            if (!_hooksAttached)
            {
                _hooksAttached = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
                if (Application.Current.ApplicationLifetime is IControlledApplicationLifetime controlled)
                    controlled.Exit += (_, _) => Stop();
            }

            if (PilotRuntime.ResolveStartMinimized(pilotOptions))
                ScheduleMinimize();
        }
    }

    public static void Stop() => Runtime.Stop(Log);

    private static T InvokeOnUi<T>(Func<T> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return func();
        return Dispatcher.UIThread.Invoke(func);
    }

    private static object? InvokeOnUi(Func<object?> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return func();
        return Dispatcher.UIThread.Invoke(func);
    }

    private static void ScheduleMinimize()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var window = GetMainWindow();
                if (window != null) window.WindowState = WindowState.Minimized;
            }
            catch { /* best-effort */ }
        }, DispatcherPriority.Background);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private static void Log(string message) => Debug.WriteLine("[AvaloniaPilot] " + message);
}
