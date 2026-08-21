using System.Collections.Generic;
using System.Text.Json;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Media;
using UiPilot.Tools;

namespace UiPilot.Tests;

internal static class TestSupport
{
    /// <summary>
    /// Build a ToolContext with a stub backend. Tools registered for protocol tests
    /// must not call OnUi for real UI work; they just return plain values.
    /// </summary>
    public static ToolContext CreateContext(IUiBackend? backend = null) =>
        new ToolContext(backend ?? new StubBackend(), func => func());

    public static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    internal class StubBackend : IUiBackend
    {
        public string Framework => "test";
        public ElementRegistry Elements { get; } = new ElementRegistry();
        public IReadOnlyList<ElementInfo> ListWindows() => System.Array.Empty<ElementInfo>();
        public IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId) => System.Array.Empty<ElementInfo>();
        public virtual FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false) =>
            new FindPage { Elements = System.Array.Empty<ElementInfo>(), Count = 0, Total = 0, HasMore = false, Offset = offset, Limit = limit };
        public ElementInfo? Inspect(string id, bool includeChildren, int depth, IReadOnlyList<string>? propertyNames) => null;
        public virtual ElementInfo? FindAncestor(string id, string? type, int maxDepth) => null;
        public string Click(string id) => "stub";
        public string TypeText(string id, string text) => "stub";
        public string PressKeys(string? id, string keys) => "stub";
        public string Scroll(string id, double dx, double dy) => "stub";
        public string Focus(string id) => "stub";
        public string SelectItem(string id, string? text, int? index) => "stub";
        public string InvokeCommand(string id) => "stub";
        public ScreenshotData? Screenshot(string? id) => null;
        public string SetWindowState(string? id, string state, bool activate) => state;
        public virtual WindowBounds ResizeWindow(string? id, double width, double height, double? x, double? y, bool activate) =>
            new WindowBounds { X = x ?? 0, Y = y ?? 0, Width = width, Height = height, State = "normal" };
        public string BringToFront(string? id) => "normal";
        public virtual IReadOnlyList<string> GetBindingErrors(bool clear) => System.Array.Empty<string>();
        public IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId) => System.Array.Empty<LayoutIssue>();
        public bool Highlight(string id, int durationMs) => false;
        public ScreenPoint GetElementCentre(string id) => new ScreenPoint(0, 0);
        public void PrepareForRealInput(string? elementId) { }
        public virtual void Shutdown() { }
    }
}
