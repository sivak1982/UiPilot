using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace WpfPilot.Cli.Tools;

/// <summary>
/// MCP tools that forward to the in-app named-pipe server. These are the "inside the running app"
/// capabilities: query the tree, interact, screenshot, diagnose. They require an attached app.
/// </summary>
[McpServerToolType]
public sealed class ForwardingTools
{
    private readonly ConnectionManager _connection;

    public ForwardingTools(ConnectionManager connection) => _connection = connection;

    [McpServerTool(Name = "list_windows")]
    [Description("List all top-level windows of the attached WPF app with identity and bounds.")]
    public Task<string> ListWindows(CancellationToken ct) =>
        Forward("list_windows", new { }, ct);

    [McpServerTool(Name = "find_elements")]
    [Description("Search the visual tree by name, AutomationId, type, or text. Returns element summaries with handle ids.")]
    public Task<string> FindElements(
        [Description("Case-insensitive substring to match. Omit to list everything up to the limit.")] string? query = null,
        [Description("Maximum number of elements to return.")] int limit = 50,
        [Description("Optional element handle id to scope the search to a subtree.")] string? root = null,
        CancellationToken ct = default) =>
        Forward("find_elements", new { query, limit, root }, ct);

    [McpServerTool(Name = "inspect_element")]
    [Description("Get detailed info for one element by handle id, optionally including child summaries.")]
    public Task<string> InspectElement(
        [Description("Element handle id from find_elements/list_windows.")] string id,
        [Description("Include child element summaries.")] bool includeChildren = false,
        [Description("How many levels of children to include when includeChildren is true.")] int depth = 1,
        CancellationToken ct = default) =>
        Forward("inspect_element", new { id, includeChildren, depth }, ct);

    [McpServerTool(Name = "click")]
    [Description("Synthetically click an element (UI Automation invoke, then ButtonBase fallback).")]
    public Task<string> Click(
        [Description("Element handle id.")] string id,
        CancellationToken ct = default) =>
        Forward("click", new { id }, ct);

    [McpServerTool(Name = "drag")]
    [Description("Drag with the real OS mouse (press, glide, release) so hit-testing, mouse capture and Preview* handlers run. Use this for tiles, thumbs and drag/drop, which synthetic click cannot reach. Give a start (element id, or fromX/fromY screen pixels) and a destination (toId, toX/toY screen pixels, or dx/dy offset from the start).")]
    public Task<string> Drag(
        [Description("Element handle id to grab; its centre is the press point. Omit when using fromX/fromY.")] string? id = null,
        [Description("Press point X in screen pixels. Used when id is omitted.")] double? fromX = null,
        [Description("Press point Y in screen pixels. Used when id is omitted.")] double? fromY = null,
        [Description("Nudge the press point horizontally off the element centre, in pixels.")] double? grabOffsetX = null,
        [Description("Nudge the press point vertically off the element centre, in pixels.")] double? grabOffsetY = null,
        [Description("Element handle id to drop onto; its centre is the release point.")] string? toId = null,
        [Description("Release point X in screen pixels.")] double? toX = null,
        [Description("Release point Y in screen pixels.")] double? toY = null,
        [Description("Horizontal offset from the press point, in pixels. Alternative to toX/toId.")] double? dx = null,
        [Description("Vertical offset from the press point, in pixels. Alternative to toY/toId.")] double? dy = null,
        [Description("How many intermediate mouse moves to send between press and release.")] int steps = 24,
        [Description("Pause between intermediate moves, in milliseconds. Raise it if the app misses moves.")] int stepDelayMs = 12,
        [Description("Pause after release so the app can finish animating, in milliseconds.")] int settleMs = 250,
        CancellationToken ct = default) =>
        Forward("drag", new { id, fromX, fromY, grabOffsetX, grabOffsetY, toId, toX, toY, dx, dy, steps, stepDelayMs, settleMs }, ct);

    [McpServerTool(Name = "type_text")]
    [Description("Set text on a focusable input (UI Automation Value pattern or TextBox).")]
    public Task<string> TypeText(
        [Description("Element handle id.")] string id,
        [Description("Text to set.")] string text,
        CancellationToken ct = default) =>
        Forward("type_text", new { id, text }, ct);

    [McpServerTool(Name = "invoke_command")]
    [Description("Execute the ICommand bound to an element (e.g. Button.Command).")]
    public Task<string> InvokeCommand(
        [Description("Element handle id.")] string id,
        CancellationToken ct = default) =>
        Forward("invoke_command", new { id }, ct);

    [McpServerTool(Name = "get_binding_errors")]
    [Description("Return captured WPF data-binding errors/warnings from the attached app.")]
    public Task<string> GetBindingErrors(
        [Description("Clear the buffer after reading.")] bool clear = false,
        CancellationToken ct = default) =>
        Forward("get_binding_errors", new { clear }, ct);

    [McpServerTool(Name = "analyze_layout")]
    [Description("Flag zero-size and off-screen visible elements.")]
    public Task<string> AnalyzeLayout(
        [Description("Optional element handle id to scope analysis to a subtree.")] string? root = null,
        CancellationToken ct = default) =>
        Forward("analyze_layout", new { root }, ct);

    [McpServerTool(Name = "highlight_element")]
    [Description("Briefly draw a red overlay over an element so a human can see the selection.")]
    public Task<string> HighlightElement(
        [Description("Element handle id.")] string id,
        [Description("How long to show the overlay, in milliseconds.")] int durationMs = 1500,
        CancellationToken ct = default) =>
        Forward("highlight_element", new { id, durationMs }, ct);

    [McpServerTool(Name = "screenshot")]
    [Description("Capture a PNG of a window (default main window) or a specific element. Saves the PNG to a temp file and returns its path plus dimensions.")]
    public async Task<string> Screenshot(
        [Description("Optional element handle id; omit for the main window.")] string? id = null,
        CancellationToken ct = default)
    {
        var result = await _connection.SendAsync("screenshot", new { id }, ct).ConfigureAwait(false);
        var base64 = result.GetProperty("base64").GetString() ?? "";
        var width = result.GetProperty("width").GetInt32();
        var height = result.GetProperty("height").GetInt32();

        var dir = Path.Combine(Path.GetTempPath(), "wpfpilot", "shots");
        System.IO.Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(base64), ct).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { path, width, height });
    }

    private async Task<string> Forward(string method, object args, CancellationToken ct)
    {
        var result = await _connection.SendAsync(method, args, ct).ConfigureAwait(false);
        return result.ValueKind == JsonValueKind.Undefined ? "null" : result.GetRawText();
    }
}
