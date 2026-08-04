using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WpfPilot.Inspection;

/// <summary>
/// Flags common layout smells that render "invisibly wrong": zero-size visible elements and
/// elements positioned entirely outside their window. Must run on the WPF UI thread.
/// </summary>
public static class LayoutAnalyzer
{
    public static List<LayoutIssue> Analyze(ElementRegistry registry, string? rootId)
    {
        var issues = new List<LayoutIssue>();
        if (Application.Current == null) return issues;

        var roots = VisualTreeQuery.ResolveRoots(registry, rootId);
        foreach (var root in roots)
        {
            var window = FindWindow(root);
            Rect windowRect = TryScreenRect(window);

            foreach (var node in VisualTreeQuery.EnumerateDescendants(root, includeSelf: true))
            {
                if (node is not FrameworkElement fe || !fe.IsVisible) continue;

                if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
                {
                    issues.Add(Make(registry, fe, "zero_size",
                        $"Visible element has zero size ({fe.ActualWidth}x{fe.ActualHeight})."));
                    continue;
                }

                if (!windowRect.IsEmpty)
                {
                    var rect = TryScreenRect(fe);
                    if (!rect.IsEmpty && !windowRect.IntersectsWith(rect))
                        issues.Add(Make(registry, fe, "off_screen",
                            "Element is positioned entirely outside its window bounds."));
                }
            }
        }
        return issues;
    }

    private static LayoutIssue Make(ElementRegistry registry, FrameworkElement fe, string issue, string message) =>
        new LayoutIssue
        {
            Id = registry.GetOrAdd(fe),
            Type = fe.GetType().Name,
            Name = string.IsNullOrEmpty(fe.Name) ? null : fe.Name,
            Issue = issue,
            Message = message,
        };

    private static Window? FindWindow(DependencyObject obj)
    {
        var current = obj;
        while (current != null)
        {
            if (current is Window w) return w;
            current = VisualTreeHelper.GetParent(current);
        }
        return obj as Window;
    }

    private static Rect TryScreenRect(FrameworkElement? fe)
    {
        if (fe == null || !fe.IsVisible || fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
            return Rect.Empty;
        try
        {
            var origin = fe.PointToScreen(new Point(0, 0));
            return new Rect(origin.X, origin.Y, fe.ActualWidth, fe.ActualHeight);
        }
        catch
        {
            return Rect.Empty;
        }
    }
}
