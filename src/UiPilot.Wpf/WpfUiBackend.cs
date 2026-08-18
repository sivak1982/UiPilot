using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using UiPilot.Abstraction;
using UiPilot.Wpf.Inspection;
using UiPilot.Wpf.Interaction;
using UiPilot.Wpf.Media;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf;

/// <summary>WPF implementation of the shared <see cref="IUiBackend"/> contract.</summary>
internal sealed class WpfUiBackend : IUiBackend
{
    private readonly BindingDiagnostics _bindings = new BindingDiagnostics();

    public string Framework => UiFrameworks.Wpf;

    public ElementRegistry Elements { get; } = new ElementRegistry();

    public void Install() => _bindings.Install();

    public void Shutdown() => _bindings.Uninstall();

    public IReadOnlyList<ElementInfo> ListWindows() => VisualTreeQuery.ListWindows(Elements);

    public IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId) =>
        VisualTreeQuery.Find(Elements, query, limit, rootId);

    public FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false) =>
        VisualTreeQuery.FindPage(Elements, query, limit, offset, rootId, exactMatch);

    public ElementInfo? Inspect(string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames) =>
        VisualTreeQuery.Inspect(Elements, id, includeChildren, depth, propertyNames);

    public ElementInfo? FindAncestor(string id, string? type, int maxDepth) =>
        VisualTreeQuery.FindAncestor(Elements, id, type, maxDepth);

    public string Click(string id) => SyntheticInput.Click(Require(id));

    public string TypeText(string id, string text) => SyntheticInput.TypeText(Require(id), text);

    public string PressKeys(string? id, string keys) =>
        SyntheticInput.PressKeys(id == null ? null : Require(id), keys);

    public string Scroll(string id, double dx, double dy) =>
        SyntheticInput.Scroll(Require(id), dx, dy);

    public string Focus(string id)
    {
        var obj = Require(id);
        if (obj is not UIElement element)
            throw new InvalidOperationException("Target is not a UIElement.");
        element.Focus();
        Keyboard.Focus(element as IInputElement);
        return "synthetic:focus";
    }

    public string SelectItem(string id, string? text, int? index) =>
        SyntheticInput.SelectItem(Require(id), text, index);

    public string InvokeCommand(string id) => SyntheticInput.InvokeCommand(Require(id));

    public ScreenshotData? CaptureScreenshot(string? id)
    {
        var target = id == null ? null : (DependencyObject)Require(id);
        return Screenshot.Capture(target);
    }

    // Explicit interface to keep the IUiBackend name while avoiding a method/type clash.
    ScreenshotData? IUiBackend.Screenshot(string? id) => CaptureScreenshot(id);

    public string SetWindowState(string? id, string state, bool activate)
    {
        var target = id == null ? null : Require(id);
        var window = WindowControl.ResolveWindow(target)
            ?? throw new InvalidOperationException("No window to control.");
        return WindowControl.SetState(window, state, activate);
    }

    public WindowBounds ResizeWindow(string? id, double width, double height, double? x, double? y, bool activate)
    {
        var target = id == null ? null : Require(id);
        var window = WindowControl.ResolveWindow(target)
            ?? throw new InvalidOperationException("No window to resize.");
        return WindowControl.Resize(window, width, height, x, y, activate);
    }

    public string BringToFront(string? id)
    {
        var target = id == null ? null : Require(id);
        var window = WindowControl.ResolveWindow(target)
            ?? throw new InvalidOperationException("No window to bring to front.");
        return WindowControl.Foreground(window);
    }

    public IReadOnlyList<string> GetBindingErrors() => _bindings.Snapshot();

    public void ClearBindingErrors() => _bindings.Clear();

    public IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId) =>
        LayoutAnalyzer.Analyze(Elements, rootId);

    public bool Highlight(string id, int durationMs) =>
        HighlightOverlay.Highlight(Require(id), durationMs);

    public ScreenPoint GetElementCentre(string id)
    {
        var obj = Require(id);
        if (obj is not Visual visual || obj is not UIElement element)
            throw new InvalidOperationException($"Element of type '{obj.GetType().Name}' has no on-screen position.");
        if (!element.IsVisible)
            throw new InvalidOperationException("Element is not visible, so it cannot be pointed at.");

        var frameworkElement = obj as FrameworkElement;
        var width = frameworkElement?.ActualWidth ?? 0;
        var height = frameworkElement?.ActualHeight ?? 0;
        var pt = visual.PointToScreen(new Point(width / 2, height / 2));
        return new ScreenPoint(pt.X, pt.Y);
    }

    public void PrepareForRealInput(string? elementId)
    {
        var target = elementId == null ? null : Require(elementId);
        var window = WindowControl.ResolveWindow(target);
        if (window != null)
            WindowControl.Foreground(window);
    }

    private DependencyObject Require(string id)
    {
        var obj = Elements.Resolve<DependencyObject>(id);
        if (obj == null)
            throw new PilotToolException(PilotErrorCodes.StaleElement, $"Unknown or collected element '{id}'.");
        return obj;
    }

    private static PilotToolException Unsupported(string message) =>
        new PilotToolException(PilotErrorCodes.Unsupported, message);
}
