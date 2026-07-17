using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfPilot.Media;

public sealed class ScreenshotData
{
    [JsonPropertyName("format")] public string Format { get; set; } = "png";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("base64")] public string Base64 { get; set; } = "";
}

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
