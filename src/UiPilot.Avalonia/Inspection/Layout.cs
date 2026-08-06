using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

internal static class Layout
{
    private const int MaxOverlapElements = 200;

    public static List<LayoutIssue> Analyze(ElementRegistry registry, string? rootId)
    {
        var issues = new List<LayoutIssue>();
        var roots = VisualTree.ResolveRoots(registry, rootId);
        foreach (var root in roots)
        {
            var window = WindowOps.Resolve(root);
            var windowRect = TryScreenRect(window);
            var hasWindow = windowRect.Width > 0 && windowRect.Height > 0;
            var visible = new List<(Control Control, Rect Rect)>();
            var overlapTruncated = false;

            foreach (var node in VisualTree.EnumerateDescendants(root, includeSelf: true))
            {
                if (node is not Control control || !control.IsVisible) continue;

                if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                {
                    issues.Add(Make(registry, control, "zero_size",
                        $"Visible element has zero size ({control.Bounds.Width}x{control.Bounds.Height})."));
                    continue;
                }

                var rect = TryScreenRect(control);
                if (hasWindow && rect.Width > 0 && rect.Height > 0 && !windowRect.Intersects(rect))
                {
                    issues.Add(Make(registry, control, "off_screen",
                        "Element is positioned entirely outside its window bounds."));
                    continue;
                }

                if (rect.Width > 0 && rect.Height > 0)
                {
                    if (visible.Count < MaxOverlapElements)
                        visible.Add((control, rect));
                    else
                        overlapTruncated = true;
                }
            }

            for (var i = 0; i < visible.Count; i++)
            {
                for (var j = i + 1; j < visible.Count; j++)
                {
                    var a = visible[i];
                    var b = visible[j];
                    if (!a.Rect.Intersects(b.Rect)) continue;
                    if (Contains(a.Rect, b.Rect) || Contains(b.Rect, a.Rect)) continue;
                    issues.Add(Make(registry, b.Control, "overlap",
                        $"Overlaps {a.Control.GetType().Name} ({registry.GetOrAdd(a.Control)})."));
                }
            }

            if (overlapTruncated)
            {
                issues.Add(new LayoutIssue
                {
                    Type = "Layout",
                    Issue = "truncated",
                    Message = $"Overlap checks were limited to the first {MaxOverlapElements} visible elements.",
                });
            }
        }
        return issues;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y &&
        outer.X + outer.Width >= inner.X + inner.Width &&
        outer.Y + outer.Height >= inner.Y + inner.Height;

    private static LayoutIssue Make(ElementRegistry registry, Control control, string issue, string message) =>
        new LayoutIssue
        {
            Id = registry.GetOrAdd(control),
            Type = control.GetType().Name,
            Name = string.IsNullOrEmpty(control.Name) ? null : control.Name,
            Issue = issue,
            Message = message,
        };

    private static Rect TryScreenRect(Control? control)
    {
        if (control == null || !control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return default;
        try
        {
            var origin = control.PointToScreen(new Point(0, 0));
            return new Rect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
        }
        catch
        {
            return default;
        }
    }
}
