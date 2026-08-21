using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf.Inspection;

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
        return new List<ElementInfo>(FindPage(registry, query, limit, 0, rootId).Elements);
    }

    /// <summary>
    /// Finds elements whose type, name, tooltip, AutomationId, or text matches <paramref name="query"/>.
    /// <paramref name="exactMatch"/> requires whole-value equality, which is what state assertions need:
    /// a substring "Initialized" also matches a "Not Initialized" label.
    /// </summary>
    public static FindPage FindPage(
        ElementRegistry registry,
        string? query,
        int limit,
        int offset,
        string? rootId,
        bool exactMatch = false)
    {
        var roots = ResolveRoots(registry, rootId);
        var q = query?.Trim();
        var hasQuery = !string.IsNullOrEmpty(q);

        // Ancestor containers can incidentally match on their .NET type name (e.g. a
        // 'MainLoadPort' user control matching a "load" query) and, being ancestors, are
        // always visited before the descendant the caller actually meant (e.g. its "Load"
        // button). When any node is an exact AutomationId match, prefer those exclusively so
        // a precise identifier always wins over an incidental substring match elsewhere.
        var exact = new List<DependencyObject>();
        var loose = new List<DependencyObject>();
        foreach (var root in roots)
        {
            foreach (var node in EnumerateDescendants(root, includeSelf: true))
            {
                if (!hasQuery)
                {
                    loose.Add(node);
                }
                else if (IsExactAutomationIdMatch(node, q!))
                {
                    exact.Add(node);
                }
                else if (Matches(node, q!, exactMatch))
                {
                    loose.Add(node);
                }
            }
        }

        var matches = exact.Count > 0 ? exact : loose;
        return FindPagePaging.Slice(matches, offset, limit, node => BuildInfo(node, registry));
    }

    public static ElementInfo? Inspect(
        ElementRegistry registry,
        string id,
        bool includeChildren,
        int depth,
        IReadOnlyList<string>? propertyNames = null)
    {
        var obj = registry.Resolve<DependencyObject>(id);
        if (obj == null) return null;
        var info = BuildInfo(obj, registry);
        if (includeChildren)
            info.Children = BuildChildren(obj, registry, Math.Max(1, depth));
        if (propertyNames != null && propertyNames.Count > 0)
            info.Properties = ReadProperties(obj, info, propertyNames);
        return info;
    }

    public static ElementInfo? FindAncestor(
        ElementRegistry registry,
        string id,
        string? type,
        int maxDepth)
    {
        var obj = registry.Resolve<DependencyObject>(id);
        if (obj == null) return null;

        var wanted = type?.Trim();
        var current = GetParent(obj);
        for (var depth = 0; current != null && depth < maxDepth; depth++)
        {
            if (string.IsNullOrEmpty(wanted) ||
                string.Equals(current.GetType().Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return BuildInfo(current, registry);
            }

            current = GetParent(current);
        }

        return null;
    }

    /// <summary>Visual parent when there is one; logical parent keeps popup/menu content walkable.</summary>
    private static DependencyObject? GetParent(DependencyObject obj)
    {
        if (IsVisual(obj))
        {
            var visual = VisualTreeHelper.GetParent(obj);
            if (visual != null) return visual;
        }

        return LogicalTreeHelper.GetParent(obj);
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
            var obj = registry.Resolve<DependencyObject>(rootId);
            if (obj != null) roots.Add(obj);
            return roots;
        }
        if (Application.Current != null)
            foreach (Window window in Application.Current.Windows)
                roots.Add(window);
        return roots;
    }

    private static bool IsExactAutomationIdMatch(DependencyObject obj, string query) =>
        string.Equals(AutomationProperties.GetAutomationId(obj), query, StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(DependencyObject obj, string query, bool exact = false)
    {
        if (IsMatch(obj.GetType().Name, query, exact)) return true;
        if (obj is FrameworkElement fe)
        {
            if (IsMatch(fe.Name, query, exact)) return true;
            // Icon-only toolbar buttons often expose their label only via ToolTip.
            if (IsMatch(fe.ToolTip as string, query, exact)) return true;
            if (fe.ToolTip is FrameworkElement tipFe && IsMatch(GetText(tipFe), query, exact)) return true;
        }
        if (IsMatch(AutomationProperties.GetAutomationId(obj), query, exact)) return true;
        if (IsMatch(GetText(obj), query, exact)) return true;
        return false;
    }

    private static bool IsMatch(string? value, string query, bool exact) =>
        exact
            ? string.Equals(value, query, StringComparison.OrdinalIgnoreCase)
            : Contains(value, query);

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
        if (obj is Visual visual && obj is UIElement uiElement && uiElement.IsVisible)
        {
            double width = 0, height = 0;
            if (obj is FrameworkElement fe)
            {
                width = fe.ActualWidth;
                height = fe.ActualHeight;
            }

            try
            {
                // PointToScreen returns physical pixels; convert size the same way so X/Y/W/H share one unit.
                var origin = visual.PointToScreen(new Point(0, 0));
                var corner = visual.PointToScreen(new Point(width, height));
                PhysicalBounds.SetFromScreenCorners(info, origin.X, origin.Y, corner.X, corner.Y);
            }
            catch
            {
                var dpi = VisualTreeHelper.GetDpi(visual);
                PhysicalBounds.SetPhysicalSizeOnly(info, width, height, dpi.DpiScaleX, dpi.DpiScaleY);
            }
        }
        else if (obj is FrameworkElement fallback)
        {
            var dpi = VisualTreeHelper.GetDpi(fallback);
            PhysicalBounds.SetPhysicalSizeOnly(
                info, fallback.ActualWidth, fallback.ActualHeight, dpi.DpiScaleX, dpi.DpiScaleY);
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

    private static Dictionary<string, string?> ReadProperties(
        DependencyObject obj,
        ElementInfo info,
        IReadOnlyList<string> propertyNames)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in propertyNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            values[name] = ReadProperty(obj, info, name);
        }
        return values;
    }

    private static string? ReadProperty(DependencyObject obj, ElementInfo info, string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "id": return info.Id;
            case "type": return info.Type;
            case "name": return info.Name;
            case "automationid": return info.AutomationId;
            case "text": return info.Text;
            case "enabled": return info.Enabled.ToString();
            case "visible": return info.Visible.ToString();
            case "childcount": return info.ChildCount.ToString();
            case "x": return info.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "y": return info.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "width": return info.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case "height": return info.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var prop = obj.GetType().GetProperty(name, System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase);
        if (prop == null || prop.GetIndexParameters().Length != 0)
            return null;

        try { return prop.GetValue(obj, null)?.ToString(); }
        catch { return null; }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
