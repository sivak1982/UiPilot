using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPilot.Media;

namespace WpfPilot.Media;

/// <summary>
/// Renders a window (or any element) to a PNG using <see cref="RenderTargetBitmap"/>. Because it
/// draws the live visual, it works even when the window is occluded. Run on the WPF UI thread.
/// </summary>
public static class Screenshot
{
    public static ScreenshotData? Capture(DependencyObject? target)
    {
        var element = target as FrameworkElement ?? ResolveDefaultWindow();
        if (element == null) return null;
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;

        var dpi = VisualTreeHelper.GetDpi(element);
        var pixelWidth = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
        var pixelHeight = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0) return null;

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return new ScreenshotData
        {
            Width = pixelWidth,
            Height = pixelHeight,
            Base64 = Convert.ToBase64String(ms.ToArray()),
        };
    }

    private static FrameworkElement? ResolveDefaultWindow()
    {
        if (Application.Current == null) return null;
        if (Application.Current.MainWindow != null) return Application.Current.MainWindow;
        foreach (Window w in Application.Current.Windows)
            return w;
        return null;
    }
}
