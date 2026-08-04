using System.Collections.Generic;
using WpfPilot.Inspection;
using WpfPilot.Media;

namespace WpfPilot.Abstraction;

/// <summary>
/// Framework-specific automation surface. WPF and Avalonia each supply an implementation;
/// the shared tool registry and pipe protocol stay identical.
/// </summary>
public interface IUiBackend
{
    /// <summary>Discovery label, e.g. <c>wpf</c> or <c>avalonia</c>.</summary>
    string Framework { get; }

    /// <summary>Weak element handles shared with the agent across tool calls.</summary>
    ElementRegistry Elements { get; }

    IReadOnlyList<ElementInfo> ListWindows();

    IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId);

    ElementInfo? Inspect(string id, bool includeChildren, int depth);

    string Click(string id);

    string TypeText(string id, string text);

    string InvokeCommand(string id);

    ScreenshotData? Screenshot(string? id);

    string SetWindowState(string? id, string state, bool activate);

    string BringToFront(string? id);

    IReadOnlyList<string> GetBindingErrors();

    void ClearBindingErrors();

    IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId);

    bool Highlight(string id, int durationMs);

    /// <summary>Screen-pixel centre of an element, for real OS mouse input.</summary>
    ScreenPoint GetElementCentre(string id);

    /// <summary>Raise the owning window so hit-testing works for a subsequent real drag.</summary>
    void PrepareForRealInput(string? elementId);

    /// <summary>Tear down framework hooks (binding listeners, etc.).</summary>
    void Shutdown();
}

/// <summary>Screen coordinate in pixels (framework-agnostic stand-in for WPF/Avalonia Point).</summary>
public readonly struct ScreenPoint
{
    public ScreenPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }
}
