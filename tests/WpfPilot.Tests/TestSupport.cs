using System.Collections.Generic;
using System.Text.Json;
using WpfPilot.Abstraction;
using WpfPilot.Inspection;
using WpfPilot.Media;
using WpfPilot.Tools;

namespace WpfPilot.Tests;

internal static class TestSupport
{
    /// <summary>
    /// Build a ToolContext with a stub backend. Tools registered for protocol tests
    /// must not call OnUi for real UI work; they just return plain values.
    /// </summary>
    public static ToolContext CreateContext() =>
        new ToolContext(new StubBackend(), func => func());

    public static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private sealed class StubBackend : IUiBackend
    {
        public string Framework => "test";
        public ElementRegistry Elements { get; } = new ElementRegistry();
        public IReadOnlyList<ElementInfo> ListWindows() => System.Array.Empty<ElementInfo>();
        public IReadOnlyList<ElementInfo> Find(string? query, int limit, string? rootId) => System.Array.Empty<ElementInfo>();
        public ElementInfo? Inspect(string id, bool includeChildren, int depth) => null;
        public string Click(string id) => "stub";
        public string TypeText(string id, string text) => "stub";
        public string InvokeCommand(string id) => "stub";
        public ScreenshotData? Screenshot(string? id) => null;
        public string SetWindowState(string? id, string state, bool activate) => state;
        public string BringToFront(string? id) => "normal";
        public IReadOnlyList<string> GetBindingErrors() => System.Array.Empty<string>();
        public void ClearBindingErrors() { }
        public IReadOnlyList<LayoutIssue> AnalyzeLayout(string? rootId) => System.Array.Empty<LayoutIssue>();
        public bool Highlight(string id, int durationMs) => false;
        public ScreenPoint GetElementCentre(string id) => new ScreenPoint(0, 0);
        public void PrepareForRealInput(string? elementId) { }
        public void Shutdown() { }
    }
}
