using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WpfPilot.Inspection;

/// <summary>
/// Flags common layout smells: zero-size, off-screen, and overlapping sibling controls.
/// Must run on the WPF UI thread.
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
            var visible = new List<(FrameworkElement Fe, Rect Rect)>();

            foreach (var node in VisualTreeQuery.EnumerateDescendants(root, includeSelf: true))
            {
                if (node is not FrameworkElement fe || !fe.IsVisible) continue;

                if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
                {
                    issues.Add(Make(registry, fe, "zero_size",
                        $"Visible element has zero size ({fe.ActualWidth}x{fe.ActualHeight})."));
                    continue;
                }

                var rect = TryScreenRect(fe);
                if (!windowRect.IsEmpty && !rect.IsEmpty && !windowRect.IntersectsWith(rect))
                {
                    issues.Add(Make(registry, fe, "off_screen",
                        "Element is positioned entirely outside its window bounds."));
                    continue;
                }

                if (!rect.IsEmpty)
                    visible.Add((fe, rect));
            }

            // Pairwise overlap among leaf-ish controls (skip nesting: parent contains child).
            for (var i = 0; i < visible.Count; i++)
            {
                for (var j = i + 1; j < visible.Count; j++)
                {
                    var a = visible[i];
                    var b = visible[j];
                    if (!a.Rect.IntersectsWith(b.Rect)) continue;
                    if (Contains(a.Rect, b.Rect) || Contains(b.Rect, a.Rect)) continue;
                    // Only report once per pair, attached to the later element.
                    issues.Add(Make(registry, b.Fe, "overlap",
                        $"Overlaps {a.Fe.GetType().Name} ({registry.GetOrAdd(a.Fe)})."));
                }
            }
        }
        return issues;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top &&
        outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

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
