using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

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

    /// <summary>
    /// Restore to normal if needed, apply size (and optional position), then return applied bounds.
    /// </summary>
    public static WindowBounds Resize(Window window, double width, double height, double? x, double? y, bool activate)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");

        if (window.WindowState != WindowState.Normal)
            window.WindowState = WindowState.Normal;

        window.Width = width;
        window.Height = height;

        if (x.HasValue || y.HasValue)
        {
            var pos = window.Position;
            var newX = x.HasValue ? (int)Math.Round(x.Value) : pos.X;
            var newY = y.HasValue ? (int)Math.Round(y.Value) : pos.Y;
            window.Position = new PixelPoint(newX, newY);
        }

        if (activate)
            Foreground(window);

        var applied = window.Position;
        return new WindowBounds
        {
            X = applied.X,
            Y = applied.Y,
            Width = window.Width,
            Height = window.Height,
            State = window.WindowState.ToString().ToLowerInvariant(),
        };
    }
}
