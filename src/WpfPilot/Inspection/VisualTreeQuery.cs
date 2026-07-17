using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfPilot.Inspection;

/// <summary>
/// Query-first visual tree access. Returns summaries (identity + bounds) and only expands a
/// full subtree for a specific selected node, so large trees never blow the agent's context.
/// All methods must be called on the WPF UI thread.
/// </summary>
public static class VisualTreeQuery
{
    public static List<ElementInfo> ListWindows(ElementRegistry registry)
    {
        var result = new List<ElementInfo>();
        if (Application.Current == null) return result;
        foreach (Window window in Application.Current.Windows)
            result.Add(BuildInfo(window, registry));
        return result;
    }

    public static List<ElementInfo> Find(ElementRegistry registry, string? query, int limit, string? rootId)
    {
        var results = new List<ElementInfo>();
        var roots = ResolveRoots(registry, rootId);
        var q = query?.Trim();
        var hasQuery = !string.IsNullOrEmpty(q);

        foreach (var root in roots)
        {
            foreach (var node in EnumerateDescendants(root, includeSelf: true))
            {
                if (results.Count >= limit) return results;
                if (!hasQuery || Matches(node, q!))
                    results.Add(BuildInfo(node, registry));
            }
        }
        return results;
    }

    public static ElementInfo? Inspect(ElementRegistry registry, string id, bool includeChildren, int depth)
    {
        var obj = registry.Resolve(id);
        if (obj == null) return null;
        var info = BuildInfo(obj, registry);
        if (includeChildren)
            info.Children = BuildChildren(obj, registry, Math.Max(1, depth));
        return info;
    }

    private static List<ElementInfo>? BuildChildren(DependencyObject obj, ElementRegistry registry, int depth)
    {
        if (depth <= 0 || !IsVisual(obj)) return null;
        var count = VisualTreeHelper.GetChildrenCount(obj);
        if (count == 0) return null;
        var list = new List<ElementInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            var childInfo = BuildInfo(child, registry);
            childInfo.Children = BuildChildren(child, registry, depth - 1);
            list.Add(childInfo);
        }
        return list;
    }

    public static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root, bool includeSelf)
    {
        return Walk(root, includeSelf, new HashSet<DependencyObject>());
    }

    /// <summary>
    /// Depth-first walk over both the visual and logical trees (deduped). Traversing the logical
    /// tree surfaces elements that aren't visually realized yet - most importantly submenu
    /// <see cref="System.Windows.Controls.MenuItem"/>s and unselected tab/content - so agents can
    /// find and drive menu-based navigation without first opening every popup.
    /// </summary>
    private static IEnumerable<DependencyObject> Walk(DependencyObject node, bool includeSelf, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(node)) yield break;
        if (includeSelf) yield return node;

        if (IsVisual(node))
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                foreach (var d in Walk(child, includeSelf: true, seen))
                    yield return d;
            }
        }

        foreach (var logical in LogicalTreeHelper.GetChildren(node))
        {
            if (logical is DependencyObject dob && !seen.Contains(dob))
                foreach (var d in Walk(dob, includeSelf: true, seen))
                    yield return d;
        }
    }

    internal static List<DependencyObject> ResolveRoots(ElementRegistry registry, string? rootId)
    {
        var roots = new List<DependencyObject>();
        if (!string.IsNullOrEmpty(rootId))
        {
            var obj = registry.Resolve(rootId);
            if (obj != null) roots.Add(obj);
            return roots;
        }
        if (Application.Current != null)
            foreach (Window window in Application.Current.Windows)
                roots.Add(window);
        return roots;
    }

    internal static bool Matches(DependencyObject obj, string query)
    {
        if (Contains(obj.GetType().Name, query)) return true;
        if (obj is FrameworkElement fe)
        {
            if (Contains(fe.Name, query)) return true;
            // Icon-only toolbar buttons often expose their label only via ToolTip.
            if (Contains(fe.ToolTip as string, query)) return true;
            if (fe.ToolTip is FrameworkElement tipFe && Contains(GetText(tipFe), query)) return true;
        }
        if (Contains(AutomationProperties.GetAutomationId(obj), query)) return true;
        if (Contains(GetText(obj), query)) return true;
        return false;
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value!.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    internal static ElementInfo BuildInfo(DependencyObject obj, ElementRegistry registry)
    {
        var info = new ElementInfo
        {
            Id = registry.GetOrAdd(obj),
            Type = obj.GetType().Name,
            AutomationId = NullIfEmpty(AutomationProperties.GetAutomationId(obj)),
            Text = NullIfEmpty(GetText(obj)),
        };

        if (obj is FrameworkElement fe)
            info.Name = NullIfEmpty(fe.Name);

        // Prefer explicit content/header text; fall back to ToolTip so icon-only buttons
        // are discoverable by their label (e.g. ToolTip="New Script").
        if (string.IsNullOrEmpty(info.Text) && obj is FrameworkElement tipSource)
            info.Text = NullIfEmpty(tipSource.ToolTip as string);

        if (obj is UIElement ui)
        {
            info.Enabled = ui.IsEnabled;
            info.Visible = ui.IsVisible;
        }

        if (IsVisual(obj))
            info.ChildCount = VisualTreeHelper.GetChildrenCount(obj);

        FillBounds(obj, info);
        return info;
    }

    private static void FillBounds(DependencyObject obj, ElementInfo info)
    {
        if (obj is FrameworkElement fe)
        {
            info.Width = fe.ActualWidth;
            info.Height = fe.ActualHeight;
        }

        if (obj is Visual visual && obj is UIElement uiElement && uiElement.IsVisible)
        {
            try
            {
                var origin = visual.PointToScreen(new Point(0, 0));
                info.X = origin.X;
                info.Y = origin.Y;
            }
            catch
            {
                // PointToScreen throws when the element is not connected to a presentation source.
            }
        }
    }

    internal static string? GetText(DependencyObject obj)
    {
        switch (obj)
        {
            case Window window:
                return window.Title;
            case TextBlock tb:
                return tb.Text;
            case TextBox txt:
                return txt.Text;
            case HeaderedItemsControl hic when hic.Header is string his:
                return his;
            case ContentControl cc when cc.Content is string s:
                return s;
            case HeaderedContentControl hcc when hcc.Header is string hs:
                return hs;
            default:
                return null;
        }
    }

    private static bool IsVisual(DependencyObject obj) => obj is Visual || obj is Visual3D;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
