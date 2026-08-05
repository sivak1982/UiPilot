using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using WpfPilot.Media;

namespace AvaloniaPilot;

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

        var pixelWidth = (int)Math.Ceiling(width);
        var pixelHeight = (int)Math.Ceiling(height);
        var bitmap = new RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96));
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
