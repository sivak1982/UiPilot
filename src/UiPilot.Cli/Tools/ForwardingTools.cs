using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Client;
using UiPilot.Cli.Status;
using UiPilot.Tools;

namespace UiPilot.Cli.Tools;

/// <summary>
/// MCP tools that forward to an in-app MCP-over-pipe server. These are the "inside the running app"
/// capabilities: query the tree, interact, screenshot, diagnose. They require an attached session.
/// Pass <c>session</c> when multiple apps are attached, or call <c>select_session</c> first.
/// </summary>
[McpServerToolType]
public sealed class ForwardingTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConnectionManager _connection;
    private readonly OperationTelemetry _telemetry;

    public ForwardingTools(ConnectionManager connection, OperationTelemetry telemetry)
    {
        _connection = connection;
        _telemetry = telemetry;
    }

    [McpServerTool(Name = ToolCatalog.ListWindows)]
    [Description("List all top-level windows of the target session's app with identity and bounds. Result includes session.")]
    public Task<CallToolResult> ListWindows(
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.ListWindows, new { }, session, ct);

    [McpServerTool(Name = ToolCatalog.FindElements)]
    [Description("Search the visual tree by name, AutomationId, type, or text. Returns element summaries with handle ids plus hasMore for pagination. Result includes session.")]
    public Task<CallToolResult> FindElements(
        [Description("Case-insensitive substring to match. Omit to list everything up to the limit.")] string? query = null,
        [Description("Maximum number of elements to return.")] int limit = 50,
        [Description("Number of matching elements to skip before returning this page.")] int offset = 0,
        [Description("Optional element handle id to scope the search to a subtree.")] string? root = null,
        [Description("When true, the query must equal a whole name/AutomationId/type/text instead of matching a substring. Use it to tell apart labels that contain each other, e.g. 'Initialized' vs 'Not Initialized'.")] bool exact = false,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.FindElements, new { query, limit, offset, root, exact }, session, ct);

    [McpServerTool(Name = ToolCatalog.InspectElement)]
    [Description("Get detailed info for one element by handle id, optionally including child summaries and named properties. Result includes session.")]
    public Task<CallToolResult> InspectElement(
        [Description("Element handle id from find_elements/list_windows.")] string id,
        [Description("Include child element summaries.")] bool includeChildren = false,
        [Description("How many levels of children to include when includeChildren is true.")] int depth = 1,
        [Description("Optional comma-separated property names to include in the inspection result.")] string? properties = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.InspectElement, new { id, includeChildren, depth, properties = ParseProperties(properties) }, session, ct);

    [McpServerTool(Name = ToolCatalog.FindAncestor)]
    [Description("Walk up from an element to the nearest ancestor of a given type. Use it when a search matched a templated control's label (a nested TextBlock) but the ancestor is what carries the enabled state and handles the click. Result includes session.")]
    public Task<CallToolResult> FindAncestor(
        [Description("Element handle id to start walking up from.")] string id,
        [Description("Ancestor type name to stop at, e.g. 'Button'. Omit to return the immediate parent.")] string? type = null,
        [Description("How many levels to walk up before giving up.")] int maxDepth = 25,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.FindAncestor, new { id, type, maxDepth }, session, ct);

    [McpServerTool(Name = ToolCatalog.WaitForElement)]
    [Description("Poll until the first matching element appears, returning the same paged shape as find_elements. Result includes session.")]
    public Task<CallToolResult> WaitForElement(
        [Description("Case-insensitive substring to match by name, AutomationId, type, or text.")] string query,
        [Description("Optional element handle id to scope polling to a subtree.")] string? root = null,
        [Description("Maximum time to wait, in milliseconds.")] int timeoutMs = 10000,
        [Description("Delay between polls, in milliseconds.")] int pollMs = 200,
        [Description("When true, the query must equal a whole name/AutomationId/type/text instead of matching a substring. Use it to tell apart labels that contain each other, e.g. 'Initialized' vs 'Not Initialized'.")] bool exact = false,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.WaitForElement, new { query, root, timeoutMs, pollMs, exact }, session, ct);

    [McpServerTool(Name = ToolCatalog.Click)]
    [Description("Synthetically click an element (automation invoke / control click fallback). Result includes session.")]
    public Task<CallToolResult> Click(
        [Description("Element handle id.")] string id,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Click, new { id }, session, ct);

    [McpServerTool(Name = ToolCatalog.Drag)]
    [Description("Drag with the real OS mouse (press, glide, release) so hit-testing, mouse capture and Preview* handlers run. Use this for tiles, thumbs and drag/drop, which synthetic click cannot reach. Give a start (element id, or fromX/fromY screen pixels) and a destination (toId, toX/toY screen pixels, or dx/dy offset from the start). Result includes session.")]
    public Task<CallToolResult> Drag(
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
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Drag, new { id, fromX, fromY, grabOffsetX, grabOffsetY, toId, toX, toY, dx, dy, steps, stepDelayMs, settleMs }, session, ct);

    [McpServerTool(Name = ToolCatalog.TypeText)]
    [Description("Set text on a focusable input (UI Automation Value pattern or TextBox). Result includes session.")]
    public Task<CallToolResult> TypeText(
        [Description("Element handle id.")] string id,
        [Description("Text to set.")] string text,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.TypeText, new { id, text }, session, ct);

    [McpServerTool(Name = ToolCatalog.PressKeys)]
    [Description("Send key chords (Ctrl+S, Enter, Tab, Escape) or sequential text to an optional element. Result includes session.")]
    public Task<CallToolResult> PressKeys(
        [Description("Key chord or text sequence to send.")] string keys,
        [Description("Optional element handle id to focus before sending keys.")] string? id = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.PressKeys, new { id, keys }, session, ct);

    [McpServerTool(Name = ToolCatalog.Scroll)]
    [Description("Scroll an element by horizontal and/or vertical deltas. Result includes session.")]
    public Task<CallToolResult> Scroll(
        [Description("Element handle id.")] string id,
        [Description("Horizontal scroll delta.")] double dx = 0,
        [Description("Vertical scroll delta.")] double dy = 0,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Scroll, new { id, dx, dy }, session, ct);

    [McpServerTool(Name = ToolCatalog.Focus)]
    [Description("Move keyboard focus to an element. Result includes session.")]
    public Task<CallToolResult> Focus(
        [Description("Element handle id.")] string id,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Focus, new { id }, session, ct);

    [McpServerTool(Name = ToolCatalog.SelectItem)]
    [Description("Select an item by visible text or zero-based index. Result includes session.")]
    public Task<CallToolResult> SelectItem(
        [Description("Element handle id for the selector/list control.")] string id,
        [Description("Optional visible item text to select.")] string? text = null,
        [Description("Optional zero-based item index to select.")] int? index = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.SelectItem, new { id, text, index }, session, ct);

    [McpServerTool(Name = ToolCatalog.InvokeCommand)]
    [Description("Execute the ICommand bound to an element (e.g. Button.Command). Result includes session.")]
    public Task<CallToolResult> InvokeCommand(
        [Description("Element handle id.")] string id,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.InvokeCommand, new { id }, session, ct);

    [McpServerTool(Name = ToolCatalog.SetWindowState)]
    [Description("Minimize, restore, or maximize a window and optionally activate it. Screenshots still work while minimized. Result includes session.")]
    public Task<CallToolResult> SetWindowState(
        [Description("Optional window element id; omit for the main window.")] string? id = null,
        [Description("Target state: minimized, normal, or maximized.")] string state = "normal",
        [Description("Bring the window to the foreground after setting the state.")] bool activate = false,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.SetWindowState, new { id, state, activate }, session, ct);

    [McpServerTool(Name = ToolCatalog.ResizeWindow)]
    [Description("Restore a window to normal if needed and set its width/height. Optionally move it with x/y and/or activate it. Result includes applied bounds and session.")]
    public Task<CallToolResult> ResizeWindow(
        [Description("Target window width in device-independent pixels.")] double width,
        [Description("Target window height in device-independent pixels.")] double height,
        [Description("Optional window element id; omit for the main window.")] string? id = null,
        [Description("Optional new left/X position in screen pixels.")] double? x = null,
        [Description("Optional new top/Y position in screen pixels.")] double? y = null,
        [Description("Bring the window to the foreground after resizing.")] bool activate = false,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.ResizeWindow, new { id, width, height, x, y, activate }, session, ct);

    [McpServerTool(Name = ToolCatalog.BringToFront)]
    [Description("Restore, if minimized, and bring a WPF or Avalonia window to the foreground so a human can see it. Result includes session.")]
    public Task<CallToolResult> BringToFront(
        [Description("Optional window element id; omit for the main window.")] string? id = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.BringToFront, new { id }, session, ct);

    [McpServerTool(Name = ToolCatalog.GetBindingErrors)]
    [Description("Return captured data-binding errors/warnings from the target session's app. Result includes session.")]
    public Task<CallToolResult> GetBindingErrors(
        [Description("Clear the buffer after reading.")] bool clear = false,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.GetBindingErrors, new { clear }, session, ct);

    [McpServerTool(Name = ToolCatalog.AnalyzeLayout)]
    [Description("Flag zero-size and off-screen visible elements. Result includes session.")]
    public Task<CallToolResult> AnalyzeLayout(
        [Description("Optional element handle id to scope analysis to a subtree.")] string? root = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.AnalyzeLayout, new { root }, session, ct);

    [McpServerTool(Name = ToolCatalog.HighlightElement)]
    [Description("Briefly draw a red overlay over an element so a human can see the selection. Result includes session.")]
    public Task<CallToolResult> HighlightElement(
        [Description("Element handle id.")] string id,
        [Description("How long to show the overlay, in milliseconds.")] int durationMs = 1500,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.HighlightElement, new { id, durationMs }, session, ct);

    [McpServerTool(Name = ToolCatalog.Screenshot)]
    [Description("Capture a PNG of a window (default main window) or a specific element. Saves the PNG to a temp file and returns an embedded image plus JSON path/dimensions/session.")]
    public async Task<CallToolResult> Screenshot(
        [Description("Optional element handle id; omit for the main window.")] string? id = null,
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default)
    {
        return await _telemetry.RunAsync(ToolCatalog.Screenshot, "forwarding", session, async () =>
        {
            try
            {
                var result = await _connection.SendAsync(ToolCatalog.Screenshot, new { id }, session, ct).ConfigureAwait(false);
                var base64 = result.GetProperty("base64").GetString() ?? "";
                var width = result.GetProperty("width").GetInt32();
                var height = result.GetProperty("height").GetInt32();
                var sessionName = result.TryGetProperty("session", out var sessionProp)
                    ? sessionProp.GetString()
                    : session ?? _connection.ActiveSessionName;
                var bytes = Convert.FromBase64String(base64);

                var dir = Path.Combine(Path.GetTempPath(), "uipilot", "shots");
                System.IO.Directory.CreateDirectory(dir);
                CleanupOldScreenshots(dir);
                var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

                var metadata = JsonSerializer.Serialize(new { path, width, height, session = sessionName }, Json);
                return new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        ImageContentBlock.FromBytes(bytes, "image/png"),
                        new TextContentBlock { Text = metadata },
                    },
                };
            }
            catch (Exception ex) when (ToolErrorResult.TryCreate(ex, out var error))
            {
                return error;
            }
        }).ConfigureAwait(false);
    }

    [McpServerTool(Name = "describe_app_tools")]
    [Description("Describe all built-in and custom tools currently registered by the target session's WPF or Avalonia app. Result includes session.")]
    public Task<CallToolResult> DescribeAppTools(
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default) =>
        Forward("describe", new { }, session, ct, "describe_app_tools");

    [McpServerTool(Name = "invoke_app_tool")]
    [Description("Invoke any attached-app tool by method name, passing a JSON object string as parameters. Use for custom app tools not exposed as first-class MCP tools. Result includes session.")]
    public Task<CallToolResult> InvokeAppTool(
        [Description("Tool method name registered inside the attached app.")] string method,
        [Description("JSON object string passed as the tool parameters.")] string parametersJson = "{}",
        [Description("Optional session name when multiple apps are attached.")] string? session = null,
        CancellationToken ct = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                _telemetry.RecordFailure("invoke_app_tool", "forwarding", session, PilotErrorCodes.InvalidArgs);
                return Task.FromResult(Err(PilotErrorCodes.InvalidArgs, "parametersJson must be a JSON object."));
            }
            args = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _telemetry.RecordFailure("invoke_app_tool", "forwarding", session, PilotErrorCodes.InvalidArgs);
            return Task.FromResult(Err(PilotErrorCodes.InvalidArgs, "parametersJson must be valid JSON.", ex.Message));
        }

        return Forward(method, args, session, ct, "invoke_app_tool");
    }

    private Task<CallToolResult> Forward(
        string method,
        object args,
        string? session,
        CancellationToken ct,
        string? operationName = null) =>
        _telemetry.RunAsync(operationName ?? method, "forwarding", session, async () =>
    {
        try
        {
            var result = await _connection.SendAsync(method, args, session, ct).ConfigureAwait(false);
            return Ok(result.ValueKind == JsonValueKind.Undefined ? "null" : result.GetRawText());
        }
        catch (Exception ex) when (ToolErrorResult.TryCreate(ex, out var error))
        {
            return error;
        }
    });

    private static string[]? ParseProperties(string? properties) =>
        string.IsNullOrWhiteSpace(properties)
            ? null
            : properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static CallToolResult Ok(string text) => new()
    {
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = text },
        },
    };

    private static CallToolResult Err(string code, string message, string? hint = null) => new()
    {
        IsError = true,
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = ErrorJson(code, message, hint) },
        },
    };

    private static string ErrorJson(string code, string message, string? hint = null) =>
        JsonSerializer.Serialize(new { error = true, code, message, hint }, Json);

    private static void CleanupOldScreenshots(string dir)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch
                {
                    // Best-effort temp cleanup should never make screenshot capture fail.
                }
            }
        }
        catch
        {
            // Ignore directory enumeration failures for the same reason.
        }
    }
}
