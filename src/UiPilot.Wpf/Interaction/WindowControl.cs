using System;
using System.Windows;
using System.Windows.Media;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf.Interaction;

/// <summary>
/// Controls target-window placement so an agent can keep the app out of the way (minimized) and
/// only bring it forward when a human needs to see it. Screenshots use <see cref="Media.Screenshot"/>
/// which renders the live visual tree, so they keep working while the window is minimized.
/// All methods must be called on the WPF UI thread.
/// </summary>
public static class WindowControl
{
    public static Window? ResolveWindow(DependencyObject? target)
    {
        if (target is Window w) return w;
        if (target is Visual || target is System.Windows.Media.Media3D.Visual3D)
        {
            var owner = Window.GetWindow(target!);
            if (owner != null) return owner;
        }
        if (Application.Current == null) return null;
        if (Application.Current.MainWindow != null) return Application.Current.MainWindow;
        foreach (Window win in Application.Current.Windows) return win;
        return null;
    }

    public static string Minimize(Window window)
    {
        window.WindowState = WindowState.Minimized;
        return "minimized";
    }

    public static string Restore(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        return window.WindowState.ToString().ToLowerInvariant();
    }

    /// <summary>Restore (if minimized) and pull the window to the foreground.</summary>
    public static string Foreground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        // Brief topmost flip is the reliable way to jump the Z-order without staying pinned.
        var wasTopmost = window.Topmost;
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
        return window.WindowState.ToString().ToLowerInvariant();
    }

    public static string SetState(Window window, string state, bool activate)
    {
        switch ((state ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "minimized":
            case "minimize":
            case "min":
                window.WindowState = WindowState.Minimized;
                break;
            case "maximized":
            case "maximize":
            case "max":
                window.WindowState = WindowState.Maximized;
                break;
            case "normal":
            case "restore":
            case "restored":
                window.WindowState = WindowState.Normal;
                break;
            default:
                throw new ArgumentException($"Unknown window state '{state}'. Use minimized, normal, or maximized.");
        }

        if (activate && window.WindowState != WindowState.Minimized)
            return Foreground(window);

        return window.WindowState.ToString().ToLowerInvariant();
    }
}
