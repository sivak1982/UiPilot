using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UiPilot;
using UiPilot.Hosting;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

/// <summary>
/// Entry point for Avalonia apps. Call <see cref="Start()"/> once after the application is created
/// (typically from <c>App.OnFrameworkInitializationCompleted</c>). Same pipe/MCP protocol as WPF.
/// </summary>
public static class PilotHost
{
    public const string ProtocolVersion = PilotRuntime.ProtocolVersion;

    private static readonly object Gate = new object();
    private static readonly PilotRuntime Runtime = new PilotRuntime();
    private static bool _hooksAttached;

    public static bool IsRunning => Runtime.IsRunning;

    public static ToolRegistry? Tools => Runtime.Tools;

    public static void Start() => Start((AvaloniaOptions?)null);

    public static void Start(bool force) => Start(new AvaloniaOptions { Force = force });

    public static void Start(AvaloniaOptions? options)
    {
        options ??= new AvaloniaOptions();
        lock (Gate)
        {
            if (Runtime.IsRunning)
                return;

            if (Application.Current == null)
            {
                Log("UiPilot.Avalonia cannot start: no Avalonia Application.Current.");
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

    private static void Log(string message) => Debug.WriteLine("[UiPilot.Avalonia] " + message);
}
