using System;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

internal static class Shot
{
    public static ScreenshotData? Capture(Visual? target)
    {
        var visual = target ?? WindowOps.Resolve(null);
        if (visual == null) return null;

        double width, height;
        if (visual is Control control)
        {
            width = control.Bounds.Width;
            height = control.Bounds.Height;
        }
        else
        {
            return null;
        }

        if (width <= 0 || height <= 0) return null;

        var scaling = (visual.GetVisualRoot() as IRenderRoot)?.RenderScaling ?? 1.0;
        var dpi = 96 * scaling;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scaling));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scaling));
        using var bitmap = new RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(dpi, dpi));
        bitmap.Render(visual);

        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms);
        return new ScreenshotData
        {
            Width = pixelWidth,
            Height = pixelHeight,
            Base64 = Convert.ToBase64String(ms.ToArray()),
        };
    }
}
