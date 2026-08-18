using System.Collections.Generic;
using System.Text.Json.Serialization;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Abstraction;

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

    /// <summary>
    /// Paged element search. <paramref name="exactMatch"/> requires the query to equal a whole
    /// value instead of matching a substring, so assertions can distinguish states whose labels
    /// contain one another (e.g. "Initialized" vs "Not Initialized").
    /// </summary>
    FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false);

    ElementInfo? Inspect(string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames);

    /// <summary>
    /// Walks up from <paramref name="id"/> to the nearest ancestor whose type matches
    /// <paramref name="type"/>. Templated controls surface their label as a deeply nested
    /// TextBlock, so a text search finds the label while only the ancestor carries the
    /// enabled state and the click handler.
    /// </summary>
    ElementInfo? FindAncestor(string id, string? type, int maxDepth);

    string Click(string id);

    string TypeText(string id, string text);

    string PressKeys(string? id, string keys);

    string Scroll(string id, double dx, double dy);

    string Focus(string id);

    string SelectItem(string id, string? text, int? index);

    string InvokeCommand(string id);

    ScreenshotData? Screenshot(string? id);

    string SetWindowState(string? id, string state, bool activate);

    /// <summary>
    /// Restore to normal (if minimized/maximized), set outer size, optionally move, and
    /// optionally activate. Returns the applied bounds.
    /// </summary>
    WindowBounds ResizeWindow(string? id, double width, double height, double? x, double? y, bool activate);

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

public sealed class FindPage
{
    [JsonPropertyName("elements")]
    public IReadOnlyList<ElementInfo> Elements { get; set; } = new List<ElementInfo>();

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }
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

/// <summary>Applied window placement after <see cref="IUiBackend.ResizeWindow"/>.</summary>
public sealed class WindowBounds
{
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("width")] public double Width { get; init; }
    [JsonPropertyName("height")] public double Height { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = "normal";
}
