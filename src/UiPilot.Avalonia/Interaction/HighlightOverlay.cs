using System;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

internal static class HighlightOverlay
{
    public static bool Show(Visual obj, int durationMs)
    {
        if (obj is not Control control) return false;
        var window = WindowOps.Resolve(control);
        if (window == null) return false;

        var overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(48, 255, 0, 0)),
            BorderBrush = Brushes.Red,
            BorderThickness = new global::Avalonia.Thickness(2),
            IsHitTestVisible = false,
        };

        var layer = AdornerLayer.GetAdornerLayer(control);
        if (layer == null) return false;

        AdornerLayer.SetAdornedElement(overlay, control);
        layer.Children.Add(overlay);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, durationMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            layer.Children.Remove(overlay);
        };
        timer.Start();
        return true;
    }
}
