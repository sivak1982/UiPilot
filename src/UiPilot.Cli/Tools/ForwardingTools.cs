using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Cli.Pipe;
using UiPilot.Tools;

namespace UiPilot.Cli.Tools;

/// <summary>
/// MCP tools that forward to the in-app named-pipe server. These are the "inside the running app"
/// capabilities: query the tree, interact, screenshot, diagnose. They require an attached app.
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

    public ForwardingTools(ConnectionManager connection) => _connection = connection;

    [McpServerTool(Name = ToolCatalog.ListWindows)]
    [Description("List all top-level windows of the attached app with identity and bounds.")]
    public Task<CallToolResult> ListWindows(CancellationToken ct) =>
        Forward(ToolCatalog.ListWindows, new { }, ct);

    [McpServerTool(Name = ToolCatalog.FindElements)]
    [Description("Search the visual tree by name, AutomationId, type, or text. Returns element summaries with handle ids plus hasMore for pagination.")]
    public Task<CallToolResult> FindElements(
        [Description("Case-insensitive substring to match. Omit to list everything up to the limit.")] string? query = null,
        [Description("Maximum number of elements to return.")] int limit = 50,
        [Description("Number of matching elements to skip before returning this page.")] int offset = 0,
        [Description("Optional element handle id to scope the search to a subtree.")] string? root = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.FindElements, new { query, limit, offset, root }, ct);

    [McpServerTool(Name = ToolCatalog.InspectElement)]
    [Description("Get detailed info for one element by handle id, optionally including child summaries and named properties.")]
    public Task<CallToolResult> InspectElement(
        [Description("Element handle id from find_elements/list_windows.")] string id,
        [Description("Include child element summaries.")] bool includeChildren = false,
        [Description("How many levels of children to include when includeChildren is true.")] int depth = 1,
        [Description("Optional comma-separated property names to include in the inspection result.")] string? properties = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.InspectElement, new { id, includeChildren, depth, properties = ParseProperties(properties) }, ct);

    [McpServerTool(Name = ToolCatalog.WaitForElement)]
    [Description("Poll until the first matching element appears, returning the same paged shape as find_elements.")]
    public Task<CallToolResult> WaitForElement(
        [Description("Case-insensitive substring to match by name, AutomationId, type, or text.")] string query,
        [Description("Optional element handle id to scope polling to a subtree.")] string? root = null,
        [Description("Maximum time to wait, in milliseconds.")] int timeoutMs = 10000,
        [Description("Delay between polls, in milliseconds.")] int pollMs = 200,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.WaitForElement, new { query, root, timeoutMs, pollMs }, ct);

    [McpServerTool(Name = ToolCatalog.Click)]
    [Description("Synthetically click an element (automation invoke / control click fallback).")]
    public Task<CallToolResult> Click(
        [Description("Element handle id.")] string id,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Click, new { id }, ct);

    [McpServerTool(Name = ToolCatalog.Drag)]
    [Description("Drag with the real OS mouse (press, glide, release) so hit-testing, mouse capture and Preview* handlers run. Use this for tiles, thumbs and drag/drop, which synthetic click cannot reach. Give a start (element id, or fromX/fromY screen pixels) and a destination (toId, toX/toY screen pixels, or dx/dy offset from the start).")]
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
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Drag, new { id, fromX, fromY, grabOffsetX, grabOffsetY, toId, toX, toY, dx, dy, steps, stepDelayMs, settleMs }, ct);

    [McpServerTool(Name = ToolCatalog.TypeText)]
    [Description("Set text on a focusable input (UI Automation Value pattern or TextBox).")]
    public Task<CallToolResult> TypeText(
        [Description("Element handle id.")] string id,
        [Description("Text to set.")] string text,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.TypeText, new { id, text }, ct);

    [McpServerTool(Name = ToolCatalog.PressKeys)]
    [Description("Send key chords (Ctrl+S, Enter, Tab, Escape) or sequential text to an optional element.")]
    public Task<CallToolResult> PressKeys(
        [Description("Key chord or text sequence to send.")] string keys,
        [Description("Optional element handle id to focus before sending keys.")] string? id = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.PressKeys, new { id, keys }, ct);

    [McpServerTool(Name = ToolCatalog.Scroll)]
    [Description("Scroll an element by horizontal and/or vertical deltas.")]
    public Task<CallToolResult> Scroll(
        [Description("Element handle id.")] string id,
        [Description("Horizontal scroll delta.")] double dx = 0,
        [Description("Vertical scroll delta.")] double dy = 0,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Scroll, new { id, dx, dy }, ct);

    [McpServerTool(Name = ToolCatalog.Focus)]
    [Description("Move keyboard focus to an element.")]
    public Task<CallToolResult> Focus(
        [Description("Element handle id.")] string id,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.Focus, new { id }, ct);

    [McpServerTool(Name = ToolCatalog.SelectItem)]
    [Description("Select an item by visible text or zero-based index.")]
    public Task<CallToolResult> SelectItem(
        [Description("Element handle id for the selector/list control.")] string id,
        [Description("Optional visible item text to select.")] string? text = null,
        [Description("Optional zero-based item index to select.")] int? index = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.SelectItem, new { id, text, index }, ct);

    [McpServerTool(Name = ToolCatalog.InvokeCommand)]
    [Description("Execute the ICommand bound to an element (e.g. Button.Command).")]
    public Task<CallToolResult> InvokeCommand(
        [Description("Element handle id.")] string id,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.InvokeCommand, new { id }, ct);

    [McpServerTool(Name = ToolCatalog.SetWindowState)]
    [Description("Minimize, restore, or maximize a window and optionally activate it. Screenshots still work while minimized.")]
    public Task<CallToolResult> SetWindowState(
        [Description("Optional window element id; omit for the main window.")] string? id = null,
        [Description("Target state: minimized, normal, or maximized.")] string state = "normal",
        [Description("Bring the window to the foreground after setting the state.")] bool activate = false,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.SetWindowState, new { id, state, activate }, ct);

    [McpServerTool(Name = ToolCatalog.BringToFront)]
    [Description("Restore, if minimized, and bring a WPF or Avalonia window to the foreground so a human can see it.")]
    public Task<CallToolResult> BringToFront(
        [Description("Optional window element id; omit for the main window.")] string? id = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.BringToFront, new { id }, ct);

    [McpServerTool(Name = ToolCatalog.GetBindingErrors)]
    [Description("Return captured data-binding errors/warnings from the attached app.")]
    public Task<CallToolResult> GetBindingErrors(
        [Description("Clear the buffer after reading.")] bool clear = false,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.GetBindingErrors, new { clear }, ct);

    [McpServerTool(Name = ToolCatalog.AnalyzeLayout)]
    [Description("Flag zero-size and off-screen visible elements.")]
    public Task<CallToolResult> AnalyzeLayout(
        [Description("Optional element handle id to scope analysis to a subtree.")] string? root = null,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.AnalyzeLayout, new { root }, ct);

    [McpServerTool(Name = ToolCatalog.HighlightElement)]
    [Description("Briefly draw a red overlay over an element so a human can see the selection.")]
    public Task<CallToolResult> HighlightElement(
        [Description("Element handle id.")] string id,
        [Description("How long to show the overlay, in milliseconds.")] int durationMs = 1500,
        CancellationToken ct = default) =>
        Forward(ToolCatalog.HighlightElement, new { id, durationMs }, ct);

    [McpServerTool(Name = ToolCatalog.Screenshot)]
    [Description("Capture a PNG of a window (default main window) or a specific element. Saves the PNG to a temp file and returns an embedded image plus JSON path/dimensions.")]
    public async Task<CallToolResult> Screenshot(
        [Description("Optional element handle id; omit for the main window.")] string? id = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _connection.SendAsync(ToolCatalog.Screenshot, new { id }, ct).ConfigureAwait(false);
            var base64 = result.GetProperty("base64").GetString() ?? "";
            var width = result.GetProperty("width").GetInt32();
            var height = result.GetProperty("height").GetInt32();
            var bytes = Convert.FromBase64String(base64);

            var dir = Path.Combine(Path.GetTempPath(), "uipilot", "shots");
            System.IO.Directory.CreateDirectory(dir);
            CleanupOldScreenshots(dir);
            var path = Path.Combine(dir, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);

            var metadata = JsonSerializer.Serialize(new { path, width, height }, Json);
            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    ImageContentBlock.FromBytes(bytes, "image/png"),
                    new TextContentBlock { Text = metadata },
                },
            };
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    [McpServerTool(Name = "describe_app_tools")]
    [Description("Describe all built-in and custom tools currently registered by the attached WPF or Avalonia app.")]
    public Task<CallToolResult> DescribeAppTools(CancellationToken ct = default) =>
        Forward("describe", new { }, ct);

    [McpServerTool(Name = "invoke_app_tool")]
    [Description("Invoke any attached-app tool by method name, passing a JSON object string as parameters. Use for custom app tools not exposed as first-class MCP tools.")]
    public Task<CallToolResult> InvokeAppTool(
        [Description("Tool method name registered inside the attached app.")] string method,
        [Description("JSON object string passed as the tool parameters.")] string parametersJson = "{}",
        CancellationToken ct = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Task.FromResult(Err(PilotErrorCodes.InvalidArgs, "parametersJson must be a JSON object."));
            args = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Task.FromResult(Err(PilotErrorCodes.InvalidArgs, "parametersJson must be valid JSON.", ex.Message));
        }

        return Forward(method, args, ct);
    }

    private async Task<CallToolResult> Forward(string method, object args, CancellationToken ct)
    {
        try
        {
            var result = await _connection.SendAsync(method, args, ct).ConfigureAwait(false);
            return Ok(result.ValueKind == JsonValueKind.Undefined ? "null" : result.GetRawText());
        }
        catch (Exception ex) when (TryCreateErrorResult(ex, out var error))
        {
            return error;
        }
    }

    private static string[]? ParseProperties(string? properties) =>
        string.IsNullOrWhiteSpace(properties)
            ? null
            : properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TrySerializeToolError(Exception ex, out string json)
    {
        switch (ex)
        {
            case PilotCliException cli:
                json = ErrorJson(cli.Code, cli.Message, cli.Hint);
                return true;
            case PipeRpcException pipe:
                json = ErrorJson(pipe.Code ?? $"rpc_{pipe.RpcCode}", pipe.Message, pipe.Hint);
                return true;
            default:
                json = "";
                return false;
        }
    }

    private static bool TryCreateErrorResult(Exception ex, out CallToolResult result)
    {
        if (!TrySerializeToolError(ex, out var json))
        {
            result = new CallToolResult();
            return false;
        }

        result = new CallToolResult
        {
            IsError = true,
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = json },
            },
        };
        return true;
    }

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
