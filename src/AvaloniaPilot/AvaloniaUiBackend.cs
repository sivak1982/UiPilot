using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WpfPilot;
using WpfPilot.Abstraction;
using WpfPilot.Inspection;
using WpfPilot.Media;

namespace AvaloniaPilot;

/// <summary>Avalonia implementation of the shared <see cref="IUiBackend"/> contract.</summary>
internal sealed class AvaloniaUiBackend : IUiBackend
{
    private readonly BindingDiagnostics _bindings = new BindingDiagnostics();

    public string Framework => UiFrameworks.Avalonia;

    public ElementRegistry Elements { get; } = new ElementRegistry();

    public void Install() => _bindings.Install();

    public void Shutdown() => _bindings.Uninstall();

    public IReadOnlyList<ElementInfo> ListWindows() => VisualTree.ListWindows(Elements);

    public IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId) =>
        VisualTree.Find(Elements, query, limit, rootId);

    public ElementInfo? Inspect(string id, bool includeChildren, int depth) =>
        VisualTree.Inspect(Elements, id, includeChildren, depth);

    public string Click(string id) => Input.Click(Require(id));

    public string TypeText(string id, string text) => Input.TypeText(Require(id), text);

    public string InvokeCommand(string id) => Input.InvokeCommand(Require(id));

    public ScreenshotData? Screenshot(string? id)
    {
        var target = id == null ? null : Require(id);
        return Shot.Capture(target);
    }

    public string SetWindowState(string? id, string state, bool activate)
    {
        var window = WindowOps.Resolve(id == null ? null : Require(id))
            ?? throw new InvalidOperationException("No window to control.");
        return WindowOps.SetState(window, state, activate);
    }

    public string BringToFront(string? id)
    {
        var window = WindowOps.Resolve(id == null ? null : Require(id))
            ?? throw new InvalidOperationException("No window to bring to front.");
        return WindowOps.Foreground(window);
    }

    public IReadOnlyList<string> GetBindingErrors() => _bindings.Snapshot();

    public void ClearBindingErrors() => _bindings.Clear();

    public IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId) =>
        Layout.Analyze(Elements, rootId);

    public bool Highlight(string id, int durationMs) =>
        HighlightOverlay.Show(Require(id), durationMs);

    public ScreenPoint GetElementCentre(string id)
    {
        var visual = Require(id);
        if (visual is not Visual v || visual is not Control control)
            throw new InvalidOperationException($"Element of type '{visual.GetType().Name}' has no on-screen position.");
        if (!control.IsVisible)
            throw new InvalidOperationException("Element is not visible, so it cannot be pointed at.");

        var bounds = control.Bounds;
        var topLeft = control.PointToScreen(new Point(0, 0));
        return new ScreenPoint(topLeft.X + bounds.Width / 2, topLeft.Y + bounds.Height / 2);
    }

    public void PrepareForRealInput(string? elementId)
    {
        var target = elementId == null ? null : Require(elementId);
        var window = WindowOps.Resolve(target);
        if (window != null)
            WindowOps.Foreground(window);
    }

    private Visual Require(string id)
    {
        var obj = Elements.Resolve<Visual>(id);
        if (obj == null) throw new ArgumentException($"Unknown or collected element '{id}'.");
        return obj;
    }
}

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
        var obj = registry.Resolve<Visual>(id);
        if (obj == null) return null;
        var info = BuildInfo(obj, registry);
        if (includeChildren)
            info.Children = BuildChildren(obj, registry, Math.Max(1, depth));
        return info;
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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
                yield return desktop.MainWindow;
            foreach (var w in desktop.Windows)
                if (!ReferenceEquals(w, desktop.MainWindow))
                    yield return w;
        }
    }

    internal static bool Matches(Visual obj, string query)
    {
        if (Contains(obj.GetType().Name, query)) return true;
        if (obj is Control control)
        {
            if (Contains(control.Name, query)) return true;
            if (Contains(ToolTip.GetTip(control) as string, query)) return true;
            if (Contains(Avalonia.Automation.AutomationProperties.GetAutomationId(control), query)) return true;
        }
        if (Contains(GetText(obj), query)) return true;
        return false;
    }

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
            info.AutomationId = NullIfEmpty(Avalonia.Automation.AutomationProperties.GetAutomationId(control));
            info.Enabled = control.IsEnabled;
            info.Visible = control.IsVisible;
            info.Width = control.Bounds.Width;
            info.Height = control.Bounds.Height;

            if (string.IsNullOrEmpty(info.Text))
                info.Text = NullIfEmpty(ToolTip.GetTip(control) as string);

            if (control.IsVisible)
            {
                try
                {
                    var origin = control.PointToScreen(new Point(0, 0));
                    info.X = origin.X;
                    info.Y = origin.Y;
                }
                catch
                {
                    // Not attached to a top-level yet.
                }
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
}

internal static class Input
{
    public static string Click(Visual obj)
    {
        if (obj is not Control control)
            throw new InvalidOperationException("Target is not a Control.");

        if (obj is MenuItem menuItem)
        {
            if (menuItem.ItemCount > 0)
            {
                menuItem.IsSubMenuOpen = true;
                return "synthetic:menuitem-expand";
            }

            if (menuItem.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
                menuItem.Command.Execute(menuItem.CommandParameter);
            return "synthetic:menuitem-click";
        }

        if (obj is Button button)
        {
            if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                return "synthetic:button-command";
            }

            // Raise a pointer-pressed/released pair so Click handlers run.
            control.Focus();
            var args = new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent);
            button.RaiseEvent(args);
            return "synthetic:raise-click";
        }

        if (obj is ToggleButton toggle)
        {
            toggle.IsChecked = !(toggle.IsChecked ?? false);
            return "synthetic:toggle";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support synthetic click.");
    }

    public static string TypeText(Visual obj, string text)
    {
        if (obj is not Control control)
            throw new InvalidOperationException("Target is not a Control.");

        control.Focus();

        if (obj is TextBox textBox)
        {
            textBox.Text = text ?? string.Empty;
            return "synthetic:textbox-set";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support text entry.");
    }

    public static string InvokeCommand(Visual obj)
    {
        if (obj is Button button && button.Command != null)
        {
            if (button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                return "command-executed";
            }
            return "command-cannot-execute";
        }

        if (obj is MenuItem menuItem && menuItem.Command != null)
        {
            if (menuItem.Command.CanExecute(menuItem.CommandParameter))
            {
                menuItem.Command.Execute(menuItem.CommandParameter);
                return "command-executed";
            }
            return "command-cannot-execute";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' has no bound ICommand.");
    }
}

internal static class WindowOps
{
    public static Window? Resolve(Visual? target)
    {
        if (target is Window w) return w;
        if (target != null)
        {
            var owner = target.GetVisualRoot() as Window;
            if (owner != null) return owner;
        }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow ?? desktop.Windows.FirstOrDefault();
        return null;
    }

    public static string Foreground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        var wasTopmost = window.Topmost;
        window.Topmost = true;
        window.Topmost = wasTopmost;
        window.Focus();
        return window.WindowState.ToString().ToLowerInvariant();
    }

    public static string SetState(Window window, string state, bool activate)
    {
        switch ((state ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "minimized":
            case "minimize":
            case "min":
                window.WindowState = WindowState.Minimized;
                break;
            case "maximized":
            case "maximize":
            case "max":
                window.WindowState = WindowState.Maximized;
                break;
            case "normal":
            case "restore":
            case "restored":
                window.WindowState = WindowState.Normal;
                break;
            default:
                throw new ArgumentException($"Unknown window state '{state}'. Use minimized, normal, or maximized.");
        }

        if (activate && window.WindowState != WindowState.Minimized)
            return Foreground(window);

        return window.WindowState.ToString().ToLowerInvariant();
    }
}

internal static class Shot
{
    public static ScreenshotData? Capture(Visual? target)
    {
        var visual = target ?? WindowOps.Resolve(null);
        if (visual == null) return null;

        double width, height;
        if (visual is Control control)
        {
            width = control.Bounds.Width;
            height = control.Bounds.Height;
        }
        else
        {
            return null;
        }

        if (width <= 0 || height <= 0) return null;

        var pixelWidth = (int)Math.Ceiling(width);
        var pixelHeight = (int)Math.Ceiling(height);
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96));
        bitmap.Render(visual);

        using var ms = new System.IO.MemoryStream();
        bitmap.Save(ms);
        return new ScreenshotData
        {
            Width = pixelWidth,
            Height = pixelHeight,
            Base64 = Convert.ToBase64String(ms.ToArray()),
        };
    }
}

internal static class Layout
{
    public static List<LayoutIssue> Analyze(ElementRegistry registry, string? rootId)
    {
        var issues = new List<LayoutIssue>();
        var roots = VisualTree.ResolveRoots(registry, rootId);
        foreach (var root in roots)
        {
            var window = WindowOps.Resolve(root);
            var windowRect = TryScreenRect(window);
            var hasWindow = windowRect.Width > 0 && windowRect.Height > 0;

            foreach (var node in VisualTree.EnumerateDescendants(root, includeSelf: true))
            {
                if (node is not Control control || !control.IsVisible) continue;

                if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                {
                    issues.Add(Make(registry, control, "zero_size",
                        $"Visible element has zero size ({control.Bounds.Width}x{control.Bounds.Height})."));
                    continue;
                }

                if (hasWindow)
                {
                    var rect = TryScreenRect(control);
                    if (rect.Width > 0 && rect.Height > 0 && !windowRect.Intersects(rect))
                        issues.Add(Make(registry, control, "off_screen",
                            "Element is positioned entirely outside its window bounds."));
                }
            }
        }
        return issues;
    }

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
            BorderThickness = new Thickness(2),
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

/// <summary>
/// Captures Avalonia binding / data warnings via the logging sink into a ring buffer.
/// Avalonia has no PresentationTraceSources equivalent; this is best-effort.
/// </summary>
internal sealed class BindingDiagnostics
{
    private readonly object _gate = new object();
    private readonly Queue<string> _messages = new Queue<string>();
    private readonly int _capacity;
    private Sink? _sink;

    public BindingDiagnostics(int capacity = 500) => _capacity = capacity;

    public void Install()
    {
        if (_sink != null) return;
        _sink = new Sink(this);
        Avalonia.Logging.Logger.Sink = _sink;
    }

    public void Uninstall()
    {
        if (_sink == null) return;
        if (ReferenceEquals(Avalonia.Logging.Logger.Sink, _sink))
            Avalonia.Logging.Logger.Sink = null;
        _sink = null;
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
            return new List<string>(_messages);
    }

    public void Clear()
    {
        lock (_gate)
            _messages.Clear();
    }

    private void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_gate)
        {
            _messages.Enqueue(message.Trim());
            while (_messages.Count > _capacity)
                _messages.Dequeue();
        }
    }

    private sealed class Sink : Avalonia.Logging.ILogSink
    {
        private readonly BindingDiagnostics _owner;

        public Sink(BindingDiagnostics owner) => _owner = owner;

        public bool IsEnabled(Avalonia.Logging.LogEventLevel level, string area) =>
            level >= Avalonia.Logging.LogEventLevel.Warning &&
            (area == "Binding" || area == "Data" || area == "BindingError" || string.IsNullOrEmpty(area));

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (!IsEnabled(level, area)) return;
            _owner.Add($"[{area}] {messageTemplate}");
        }

        public void Log(Avalonia.Logging.LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            if (!IsEnabled(level, area)) return;
            try
            {
                _owner.Add($"[{area}] {string.Format(messageTemplate, propertyValues)}");
            }
            catch
            {
                _owner.Add($"[{area}] {messageTemplate}");
            }
        }
    }
}
