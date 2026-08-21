using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using UiPilot.Abstraction;
using UiPilot.Inspection;

namespace UiPilot.WinForms.Inspection;

/// <summary>Query-first traversal of Controls and ToolStripItems. UI-thread only.</summary>
internal static class ControlTree
{
    public static List<ElementInfo> ListWindows(ElementRegistry registry)
    {
        var result = new List<ElementInfo>();
        foreach (Form form in Application.OpenForms)
            result.Add(BuildInfo(form, registry));
        return result;
    }

    public static List<ElementInfo> Find(ElementRegistry registry, string? query, int limit, string? rootId) =>
        new List<ElementInfo>(FindPage(registry, query, limit, 0, rootId, exactMatch: false).Elements);

    public static FindPage FindPage(
        ElementRegistry registry, string? query, int limit, int offset, string? rootId, bool exactMatch)
    {
        var q = query?.Trim();
        var hasQuery = !string.IsNullOrEmpty(q);
        var exactIds = new List<object>();
        var loose = new List<object>();
        foreach (var root in ResolveRoots(registry, rootId))
        {
            foreach (var node in EnumerateDescendants(root, true))
            {
                if (!hasQuery) loose.Add(node);
                else if (IsExactName(node, q!)) exactIds.Add(node);
                else if (Matches(node, q!, exactMatch)) loose.Add(node);
            }
        }

        var matches = exactIds.Count > 0 ? exactIds : loose;
        return FindPagePaging.Slice(matches, offset, limit, node => BuildInfo(node, registry));
    }

    public static ElementInfo? Inspect(
        ElementRegistry registry, string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames)
    {
        var obj = registry.Resolve(id);
        if (!IsElement(obj)) return null;
        var info = BuildInfo(obj!, registry);
        if (includeChildren)
            info.Children = BuildChildren(obj!, registry, Math.Max(1, depth));
        if (propertyNames is { Count: > 0 })
            info.Properties = ReadProperties(obj!, info, propertyNames);
        return info;
    }

    public static ElementInfo? FindAncestor(ElementRegistry registry, string id, string? type, int maxDepth)
    {
        var current = ParentOf(registry.Resolve(id));
        var wanted = type?.Trim();
        for (var depth = 0; current != null && depth < Math.Max(0, maxDepth); depth++)
        {
            if (string.IsNullOrEmpty(wanted) ||
                string.Equals(current.GetType().Name, wanted, StringComparison.OrdinalIgnoreCase))
                return BuildInfo(current, registry);
            current = ParentOf(current);
        }
        return null;
    }

    internal static List<object> ResolveRoots(ElementRegistry registry, string? rootId)
    {
        var result = new List<object>();
        if (!string.IsNullOrEmpty(rootId))
        {
            var root = registry.Resolve(rootId);
            if (IsElement(root)) result.Add(root!);
            return result;
        }
        foreach (Form form in Application.OpenForms)
            result.Add(form);
        return result;
    }

    internal static IEnumerable<object> EnumerateDescendants(object root, bool includeSelf)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var item in Walk(root, includeSelf, seen))
            yield return item;
    }

    private static IEnumerable<object> Walk(object node, bool includeSelf, HashSet<object> seen)
    {
        if (!seen.Add(node)) yield break;
        if (includeSelf) yield return node;
        foreach (var child in ChildrenOf(node))
            foreach (var descendant in Walk(child, true, seen))
                yield return descendant;
    }

    internal static IEnumerable<object> ChildrenOf(object node)
    {
        if (node is Control control)
        {
            foreach (Control child in control.Controls)
                yield return child;
            if (control is ToolStrip strip)
                foreach (ToolStripItem item in strip.Items)
                    yield return item;
        }
        if (node is ToolStripDropDownItem dropDown)
            foreach (ToolStripItem item in dropDown.DropDownItems)
                yield return item;
    }

    internal static object? ParentOf(object? node)
    {
        if (node is ToolStripItem item)
            return item.OwnerItem ?? (object?)item.Owner;
        return (node as Control)?.Parent;
    }

    internal static Rectangle ScreenBounds(object node)
    {
        try
        {
            if (node is Control control && control.IsHandleCreated)
                return control.RectangleToScreen(control.ClientRectangle);
            if (node is ToolStripItem item && item.Owner is { IsHandleCreated: true } owner)
            {
                var location = owner.PointToScreen(item.Bounds.Location);
                return new Rectangle(location, item.Bounds.Size);
            }
        }
        catch { }
        return Rectangle.Empty;
    }

    internal static ElementInfo BuildInfo(object obj, ElementRegistry registry)
    {
        var bounds = ScreenBounds(obj);
        var info = new ElementInfo
        {
            Id = registry.GetOrAdd(obj),
            Type = obj.GetType().Name,
            Name = NullIfEmpty(NameOf(obj)),
            AutomationId = NullIfEmpty(NameOf(obj)),
            Text = NullIfEmpty(TextOf(obj)),
            Enabled = obj is Control c ? c.Enabled : obj is ToolStripItem i && i.Enabled,
            Visible = obj is Control vc ? vc.Visible : obj is ToolStripItem vi && vi.Available,
            ChildCount = CountChildren(obj),
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
        };
        if (string.IsNullOrEmpty(info.Text))
            info.Text = NullIfEmpty(obj is Control tc ? tc.AccessibleName : (obj as ToolStripItem)?.ToolTipText);
        return info;
    }

    internal static string? TextOf(object obj) => obj switch
    {
        Form form => form.Text,
        Control control => control.Text,
        ToolStripItem item => item.Text,
        _ => null,
    };

    private static string? NameOf(object obj) => obj switch
    {
        Control control => control.Name,
        ToolStripItem item => item.Name,
        _ => null,
    };

    private static int CountChildren(object obj)
    {
        var count = obj is Control c ? c.Controls.Count : 0;
        if (obj is ToolStrip strip) count += strip.Items.Count;
        if (obj is ToolStripDropDownItem dropDown) count += dropDown.DropDownItems.Count;
        return count;
    }

    private static List<ElementInfo>? BuildChildren(object obj, ElementRegistry registry, int depth)
    {
        if (depth <= 0) return null;
        var list = new List<ElementInfo>();
        foreach (var child in ChildrenOf(obj))
        {
            var info = BuildInfo(child, registry);
            info.Children = BuildChildren(child, registry, depth - 1);
            list.Add(info);
        }
        return list.Count == 0 ? null : list;
    }

    private static bool IsElement(object? obj) => obj is Control or ToolStripItem;
    private static bool IsExactName(object obj, string query) =>
        string.Equals(NameOf(obj), query, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(object obj, string query, bool exact) =>
        Match(obj.GetType().Name, query, exact) ||
        Match(NameOf(obj), query, exact) ||
        Match(TextOf(obj), query, exact) ||
        Match(obj is Control c ? c.AccessibleName : (obj as ToolStripItem)?.ToolTipText, query, exact);

    private static bool Match(string? value, string query, bool exact) =>
        exact
            ? string.Equals(value, query, StringComparison.OrdinalIgnoreCase)
            : !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private static Dictionary<string, string?> ReadProperties(
        object obj, ElementInfo info, IReadOnlyList<string> names)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();
            values[name] = name.ToLowerInvariant() switch
            {
                "id" => info.Id,
                "type" => info.Type,
                "name" => info.Name,
                "automationid" => info.AutomationId,
                "text" => info.Text,
                "enabled" => info.Enabled.ToString(),
                "visible" => info.Visible.ToString(),
                "childcount" => info.ChildCount.ToString(CultureInfo.InvariantCulture),
                "x" => info.X.ToString(CultureInfo.InvariantCulture),
                "y" => info.Y.ToString(CultureInfo.InvariantCulture),
                "width" => info.Width.ToString(CultureInfo.InvariantCulture),
                "height" => info.Height.ToString(CultureInfo.InvariantCulture),
                _ => ReflectProperty(obj, name),
            };
        }
        return values;
    }

    private static string? ReflectProperty(object obj, string name)
    {
        var property = obj.GetType().GetProperty(
            name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null || property.GetIndexParameters().Length != 0) return null;
        try { return property.GetValue(obj)?.ToString(); }
        catch { return null; }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
