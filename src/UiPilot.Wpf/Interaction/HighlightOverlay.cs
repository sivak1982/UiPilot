using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf.Interaction;

/// <summary>Briefly draws a red overlay over an element so a human can see what the agent picked.</summary>
public static class HighlightOverlay
{
    public static bool Highlight(DependencyObject obj, int durationMs)
    {
        if (obj is not UIElement element) return false;
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer == null) return false;

        var adorner = new HighlightAdorner(element);
        layer.Add(adorner);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, durationMs)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            layer.Remove(adorner);
        };
        timer.Start();
        return true;
    }

    private sealed class HighlightAdorner : Adorner
    {
        private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(48, 255, 0, 0));
        private static readonly Pen Stroke = new Pen(new SolidColorBrush(Color.FromRgb(255, 0, 0)), 2);

        static HighlightAdorner()
        {
            Fill.Freeze();
            Stroke.Freeze();
        }

        public HighlightAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(AdornedElement.RenderSize);
            drawingContext.DrawRectangle(Fill, Stroke, rect);
        }
    }
}
