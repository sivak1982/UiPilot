using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UiPilot.Inspection;

namespace UiPilot.WinForms.Inspection;

internal static class LayoutAnalyzer
{
    private const int MaxOverlapElements = 200;

    public static IReadOnlyList<LayoutIssue> Analyze(ElementRegistry registry, string? rootId)
    {
        var issues = new List<LayoutIssue>();
        foreach (var root in ControlTree.ResolveRoots(registry, rootId))
        {
            var window = root as Form ?? (root as Control)?.FindForm() ??
                         (root as ToolStripItem)?.Owner?.FindForm();
            var windowRect = window == null ? Rectangle.Empty : ControlTree.ScreenBounds(window);
            var visible = new List<(object Element, Rectangle Bounds)>();
            var truncated = false;

            foreach (var element in ControlTree.EnumerateDescendants(root, true))
            {
                var info = ControlTree.BuildInfo(element, registry);
                if (!info.Visible) continue;
                var bounds = ControlTree.ScreenBounds(element);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    issues.Add(Make(info, "zero_size", "Visible element has zero size."));
                    continue;
                }
                if (!windowRect.IsEmpty && !windowRect.IntersectsWith(bounds))
                {
                    issues.Add(Make(info, "off_screen", "Element is positioned entirely outside its window bounds."));
                    continue;
                }
                if (visible.Count < MaxOverlapElements) visible.Add((element, bounds));
                else truncated = true;
            }

            for (var i = 0; i < visible.Count; i++)
            for (var j = i + 1; j < visible.Count; j++)
            {
                var a = visible[i];
                var b = visible[j];
                if (ControlTree.ParentOf(a.Element) != ControlTree.ParentOf(b.Element)) continue;
                if (!a.Bounds.IntersectsWith(b.Bounds)) continue;
                if (a.Bounds.Contains(b.Bounds) || b.Bounds.Contains(a.Bounds)) continue;
                var info = ControlTree.BuildInfo(b.Element, registry);
                issues.Add(Make(info, "overlap",
                    $"Overlaps {a.Element.GetType().Name} ({registry.GetOrAdd(a.Element)})."));
            }

            if (truncated)
                issues.Add(new LayoutIssue
                {
                    Type = "LayoutAnalyzer",
                    Issue = "truncated",
                    Message = $"Overlap checks were limited to the first {MaxOverlapElements} visible elements.",
                });
        }
        return issues;
    }

    private static LayoutIssue Make(ElementInfo info, string issue, string message) => new LayoutIssue
    {
        Id = info.Id,
        Type = info.Type,
        Name = info.Name,
        Issue = issue,
        Message = message,
    };
}
