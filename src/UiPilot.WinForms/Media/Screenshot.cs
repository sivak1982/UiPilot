using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UiPilot.Media;

namespace UiPilot.WinForms.Media;

/// <summary>Captures controls with DrawToBitmap and falls back to PrintWindow.</summary>
internal static class Screenshot
{
    private const uint PwRenderFullContent = 0x00000002;

    public static ScreenshotData? Capture(object? target)
    {
        if (target is ToolStripItem item)
            return CaptureItem(item);
        var control = target as Control ?? PilotHost.FirstForm();
        if (control == null) return null;
        // PrintWindow renders the non-client frame for top-level forms, so its destination must
        // use full window dimensions. Child controls and ToolStrip crops remain client-sized.
        var size = control is Form ? control.Size : control.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            size = control.Size;
        if (size.Width <= 0 || size.Height <= 0) return null;

        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        var minimized = (control as Form ?? control.FindForm())?.WindowState == FormWindowState.Minimized;
        var captured = minimized
            ? TryPrintWindow(control, bitmap) || TryDraw(control, bitmap)
            : TryDraw(control, bitmap) || TryPrintWindow(control, bitmap);
        if (!captured)
            return null;
        return Encode(bitmap);
    }

    private static ScreenshotData? CaptureItem(ToolStripItem item)
    {
        if (item.Owner == null || item.Bounds.Width <= 0 || item.Bounds.Height <= 0)
            return null;
        var ownerShot = CaptureBitmap(item.Owner);
        if (ownerShot == null) return null;
        using (ownerShot)
        {
            var crop = Rectangle.Intersect(
                new Rectangle(Point.Empty, ownerShot.Size), item.Bounds);
            if (crop.Width <= 0 || crop.Height <= 0) return null;
            using var itemBitmap = ownerShot.Clone(crop, PixelFormat.Format32bppArgb);
            return Encode(itemBitmap);
        }
    }

    private static Bitmap? CaptureBitmap(Control control)
    {
        var size = control.ClientSize;
        if (size.Width <= 0 || size.Height <= 0) return null;
        var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        if (TryDraw(control, bitmap) || TryPrintWindow(control, bitmap))
            return bitmap;
        bitmap.Dispose();
        return null;
    }

    private static bool TryDraw(Control control, Bitmap bitmap)
    {
        try
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPrintWindow(Control control, Bitmap bitmap)
    {
        if (!control.IsHandleCreated) return false;
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try { return PrintWindow(control.Handle, hdc, PwRenderFullContent); }
            finally { graphics.ReleaseHdc(hdc); }
        }
        catch
        {
            return false;
        }
    }

    private static ScreenshotData Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new ScreenshotData
        {
            Width = bitmap.Width,
            Height = bitmap.Height,
            Base64 = Convert.ToBase64String(stream.ToArray()),
        };
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
}
