using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace AvaloniaPilot;

internal static class WindowOps
{
    public static Window? Resolve(Visual? target)
    {
        if (target is Window w) return w;
        if (target != null)
        {
            var owner = target.GetVisualRoot() as Window;
            if (owner != null) return owner;
        }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow ?? desktop.Windows.FirstOrDefault();
        return null;
    }

    public static string Foreground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
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
