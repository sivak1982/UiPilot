using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UiPilot.Abstraction;

namespace UiPilot.WinForms.Interaction;

internal static class WindowControl
{
    public static Form? Resolve(object? target)
    {
        if (target is Form form) return form;
        if (target is Control control) return control.FindForm();
        if (target is ToolStripItem item) return item.Owner?.FindForm();
        return PilotHost.FirstForm();
    }

    public static string SetState(Form form, string state, bool activate)
    {
        form.WindowState = (state ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "minimized" or "minimize" or "min" => FormWindowState.Minimized,
            "maximized" or "maximize" or "max" => FormWindowState.Maximized,
            "normal" or "restore" or "restored" => FormWindowState.Normal,
            _ => throw new ArgumentException($"Unknown window state '{state}'. Use minimized, normal, or maximized."),
        };
        if (activate && form.WindowState != FormWindowState.Minimized)
            return Foreground(form);
        return StateName(form.WindowState);
    }

    public static WindowBounds Resize(
        Form form, double width, double height, double? x, double? y, bool activate)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        form.WindowState = FormWindowState.Normal;
        var left = x.HasValue ? checked((int)Math.Round(x.Value)) : form.Left;
        var top = y.HasValue ? checked((int)Math.Round(y.Value)) : form.Top;
        form.SetBounds(left, top, checked((int)Math.Round(width)), checked((int)Math.Round(height)));
        if (activate) Foreground(form);
        return new WindowBounds
        {
            X = form.Left,
            Y = form.Top,
            Width = form.Width,
            Height = form.Height,
            State = StateName(form.WindowState),
        };
    }

    public static string Foreground(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Show();
        form.Activate();
        form.BringToFront();
        if (form.IsHandleCreated)
            SetForegroundWindow(form.Handle);
        return StateName(form.WindowState);
    }

    private static string StateName(FormWindowState state) => state.ToString().ToLowerInvariant();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
