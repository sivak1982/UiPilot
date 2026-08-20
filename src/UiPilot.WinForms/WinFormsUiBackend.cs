using System;
using System.Collections.Generic;
using System.Windows.Forms;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Media;
using UiPilot.Tools;
using UiPilot.WinForms.Inspection;
using UiPilot.WinForms.Interaction;
using UiPilot.WinForms.Media;

namespace UiPilot.WinForms;

/// <summary>Windows Forms implementation of the shared <see cref="IUiBackend"/> contract.</summary>
internal sealed class WinFormsUiBackend : IUiBackend
{
    public string Framework => "winforms";
    public ElementRegistry Elements { get; } = new ElementRegistry();

    public IReadOnlyList<ElementInfo> ListWindows() => ControlTree.ListWindows(Elements);
    public IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId) =>
        ControlTree.Find(Elements, query, limit, rootId);
    public FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false) =>
        ControlTree.FindPage(Elements, query, limit, offset, rootId, exactMatch);
    public ElementInfo? Inspect(
        string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames) =>
        ControlTree.Inspect(Elements, id, includeChildren, depth, propertyNames);
    public ElementInfo? FindAncestor(string id, string? type, int maxDepth) =>
        ControlTree.FindAncestor(Elements, id, type, maxDepth);

    public string Click(string id) => SyntheticInput.Click(Require(id));
    public string TypeText(string id, string text) => SyntheticInput.TypeText(Require(id), text);
    public string PressKeys(string? id, string keys) =>
        SyntheticInput.PressKeys(id == null ? null : Require(id), keys);
    public string Scroll(string id, double dx, double dy) => SyntheticInput.Scroll(Require(id), dx, dy);
    public string Focus(string id) => SyntheticInput.Focus(Require(id));
    public string SelectItem(string id, string? text, int? index) =>
        SyntheticInput.SelectItem(Require(id), text, index);

    public string InvokeCommand(string id) => throw new PilotToolException(
        PilotErrorCodes.Unsupported,
        "invoke_command is not supported by WinForms because it has no shared ICommand model.");

    ScreenshotData? IUiBackend.Screenshot(string? id) =>
        Media.Screenshot.Capture(id == null ? null : Require(id));

    public string SetWindowState(string? id, string state, bool activate)
    {
        var form = WindowControl.Resolve(id == null ? null : Require(id))
            ?? throw new InvalidOperationException("No window to control.");
        return WindowControl.SetState(form, state, activate);
    }

    public WindowBounds ResizeWindow(
        string? id, double width, double height, double? x, double? y, bool activate)
    {
        var form = WindowControl.Resolve(id == null ? null : Require(id))
            ?? throw new InvalidOperationException("No window to resize.");
        return WindowControl.Resize(form, width, height, x, y, activate);
    }

    public string BringToFront(string? id)
    {
        var form = WindowControl.Resolve(id == null ? null : Require(id))
            ?? throw new InvalidOperationException("No window to bring to front.");
        return WindowControl.Foreground(form);
    }

    public IReadOnlyList<string> GetBindingErrors() => Array.Empty<string>();
    public void ClearBindingErrors() { }
    public IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId) =>
        LayoutAnalyzer.Analyze(Elements, rootId);
    public bool Highlight(string id, int durationMs) =>
        HighlightOverlay.Highlight(Require(id), durationMs);

    public ScreenPoint GetElementCentre(string id)
    {
        var obj = Require(id);
        var bounds = ControlTree.ScreenBounds(obj);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException($"Element of type '{obj.GetType().Name}' has no on-screen position.");
        return new ScreenPoint(bounds.Left + bounds.Width / 2d, bounds.Top + bounds.Height / 2d);
    }

    public void PrepareForRealInput(string? elementId)
    {
        var form = WindowControl.Resolve(elementId == null ? null : Require(elementId));
        if (form != null) WindowControl.Foreground(form);
    }

    public void Shutdown() { }

    private object Require(string id)
    {
        var obj = Elements.Resolve(id);
        if (obj is not Control && obj is not ToolStripItem)
            throw new PilotToolException(
                PilotErrorCodes.StaleElement, $"Unknown or collected element '{id}'.");
        return obj;
    }
}
