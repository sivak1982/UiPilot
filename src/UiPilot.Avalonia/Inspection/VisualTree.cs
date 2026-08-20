using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

internal static class VisualTree
{
    public static List<ElementInfo> ListWindows(ElementRegistry registry)
    {
        var result = new List<ElementInfo>();
        foreach (var window in EnumerateWindows())
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
        var safeLimit = Math.Max(0, limit);
        var safeOffset = Math.Max(0, offset);

        // Ancestor containers can incidentally match on their .NET type name (e.g. a
        // 'MainLoadPort' user control matching a "load" query) and, being ancestors, are
        // always visited before the descendant the caller actually meant (e.g. its "Load"
        // button). When any node is an exact AutomationId match, prefer those exclusively so
        // a precise identifier always wins over an incidental substring match elsewhere.
        var exact = new List<Visual>();
        var loose = new List<Visual>();
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
        var results = new List<ElementInfo>();
        var matched = 0;
        foreach (var node in matches)
        {
            if (matched >= safeOffset && results.Count < safeLimit)
                results.Add(BuildInfo(node, registry));
            matched++;
        }

        return new FindPage
        {
            Elements = results,
            Count = results.Count,
            Total = matched,
            HasMore = matched > safeOffset + results.Count,
            Offset = safeOffset,
            Limit = safeLimit,
        };
    }

    public static ElementInfo? Inspect(
        ElementRegistry registry,
        string id,
        bool includeChildren,
        int depth,
        IReadOnlyList<string>? propertyNames = null)
    {
        var obj = registry.Resolve<Visual>(id);
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
        var obj = registry.Resolve<Visual>(id);
        if (obj == null) return null;

        var wanted = type?.Trim();
        var current = obj.GetVisualParent();
        for (var depth = 0; current != null && depth < maxDepth; depth++)
        {
            if (string.IsNullOrEmpty(wanted) ||
                string.Equals(current.GetType().Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return BuildInfo(current, registry);
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static List<ElementInfo>? BuildChildren(Visual obj, ElementRegistry registry, int depth)
    {
        if (depth <= 0) return null;
        var children = obj.GetVisualChildren().ToList();
        if (children.Count == 0) return null;
        var list = new List<ElementInfo>(children.Count);
        foreach (var child in children)
        {
            var childInfo = BuildInfo(child, registry);
            childInfo.Children = BuildChildren(child, registry, depth - 1);
            list.Add(childInfo);
        }
        return list;
    }

    public static IEnumerable<Visual> EnumerateDescendants(Visual root, bool includeSelf) =>
        Walk(root, includeSelf, new HashSet<Visual>());

    private static IEnumerable<Visual> Walk(Visual node, bool includeSelf, HashSet<Visual> seen)
    {
        if (!seen.Add(node)) yield break;
        if (includeSelf) yield return node;

        foreach (var child in node.GetVisualChildren())
        {
            foreach (var d in Walk(child, includeSelf: true, seen))
                yield return d;
        }

        if (node is ILogical logical)
        {
            foreach (var child in logical.LogicalChildren)
            {
                if (child is Visual visual && !seen.Contains(visual))
                {
                    foreach (var d in Walk(visual, includeSelf: true, seen))
                        yield return d;
                }
            }
        }
    }

    internal static List<Visual> ResolveRoots(ElementRegistry registry, string? rootId)
    {
        var roots = new List<Visual>();
        if (!string.IsNullOrEmpty(rootId))
        {
            var obj = registry.Resolve<Visual>(rootId);
            if (obj != null) roots.Add(obj);
            return roots;
        }
        roots.AddRange(EnumerateWindows());
        return roots;
    }

    private static IEnumerable<Window> EnumerateWindows()
    {
        if (Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
                yield return desktop.MainWindow;
            foreach (var w in desktop.Windows)
                if (!ReferenceEquals(w, desktop.MainWindow))
                    yield return w;
        }
    }

    private static bool IsExactAutomationIdMatch(Visual obj, string query) =>
        obj is Control control &&
        string.Equals(AutomationProperties.GetAutomationId(control), query, StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(Visual obj, string query, bool exact = false)
    {
        if (IsMatch(obj.GetType().Name, query, exact)) return true;
        if (obj is Control control)
        {
            if (IsMatch(control.Name, query, exact)) return true;
            if (IsMatch(ToolTip.GetTip(control) as string, query, exact)) return true;
            if (IsMatch(AutomationProperties.GetAutomationId(control), query, exact)) return true;
        }
        if (IsMatch(GetText(obj), query, exact)) return true;
        return false;
    }

    private static bool IsMatch(string? value, string query, bool exact) =>
        exact
            ? string.Equals(value, query, StringComparison.OrdinalIgnoreCase)
            : Contains(value, query);

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrEmpty(value) && value!.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    internal static ElementInfo BuildInfo(Visual obj, ElementRegistry registry)
    {
        var info = new ElementInfo
        {
            Id = registry.GetOrAdd(obj),
            Type = obj.GetType().Name,
            Text = NullIfEmpty(GetText(obj)),
            ChildCount = obj.GetVisualChildren().Count(),
        };

        if (obj is Control control)
        {
            info.Name = NullIfEmpty(control.Name);
            info.AutomationId = NullIfEmpty(AutomationProperties.GetAutomationId(control));
            info.Enabled = control.IsEnabled;
            info.Visible = control.IsVisible;

            if (string.IsNullOrEmpty(info.Text))
                info.Text = NullIfEmpty(ToolTip.GetTip(control) as string);

            if (control.IsVisible)
            {
                try
                {
                    var origin = control.PointToScreen(new Point(0, 0));
                    var corner = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
                    info.X = origin.X;
                    info.Y = origin.Y;
                    info.Width = Math.Abs(corner.X - origin.X);
                    info.Height = Math.Abs(corner.Y - origin.Y);
                }
                catch
                {
                    info.Width = control.Bounds.Width;
                    info.Height = control.Bounds.Height;
                }
            }
            else
            {
                info.Width = control.Bounds.Width;
                info.Height = control.Bounds.Height;
            }
        }

        return info;
    }

    internal static string? GetText(Visual obj)
    {
        switch (obj)
        {
            case Window window:
                return window.Title;
            case TextBlock tb:
                return tb.Text;
            case TextBox txt:
                return txt.Text;
            case ContentControl cc when cc.Content is string s:
                return s;
            case HeaderedContentControl hcc when hcc.Header is string hs:
                return hs;
            default:
                return null;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Dictionary<string, string?> ReadProperties(
        Visual obj,
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

    private static string? ReadProperty(Visual obj, ElementInfo info, string name)
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
}
