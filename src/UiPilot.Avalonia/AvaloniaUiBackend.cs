using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using UiPilot;
using UiPilot.Abstraction;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

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

    public FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false) =>
        VisualTree.FindPage(Elements, query, limit, offset, rootId, exactMatch);

    public ElementInfo? Inspect(string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames) =>
        VisualTree.Inspect(Elements, id, includeChildren, depth, propertyNames);

    public string Click(string id) => Input.Click(Require(id));

    public string TypeText(string id, string text) => Input.TypeText(Require(id), text);

    public string PressKeys(string? id, string keys) =>
        Input.PressKeys(id == null ? null : Require(id), keys);

    public string Scroll(string id, double dx, double dy) =>
        Input.Scroll(Require(id), dx, dy);

    public string Focus(string id) => Input.Focus(Require(id));

    public string SelectItem(string id, string? text, int? index) =>
        Input.SelectItem(Require(id), text, index);

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
        if (visual is not Control control)
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
        if (obj == null)
            throw new PilotToolException(PilotErrorCodes.StaleElement, $"Unknown or collected element '{id}'.");
        return obj;
    }
}
