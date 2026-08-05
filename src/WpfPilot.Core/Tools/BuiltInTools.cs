using System;
using System.Diagnostics;
using System.Threading;
using WpfPilot.Abstraction;
using WpfPilot.Interaction;

namespace WpfPilot.Tools;

/// <summary>
/// Registers the built-in agent tools against an <see cref="IUiBackend"/>. Tool names and
/// argument contracts are identical for WPF and Avalonia.
/// </summary>
internal static class BuiltInTools
{
    private static readonly object DragGate = new object();

    public static void RegisterAll(ToolRegistry registry)
    {
        registry.Register(ToolCatalog.ListWindows,
            "List all top-level windows with identity and bounds.",
            (ctx, _) => ctx.OnUi(() => new { windows = ctx.Backend.ListWindows() }));

        registry.Register(ToolCatalog.FindElements,
            "Search the visual tree by name, AutomationId, type, or text. Args: query, limit=50, offset=0, root(id).",
            (ctx, args) =>
            {
                var query = args.GetString("query");
                var limit = args.GetInt("limit", 50);
                var offset = args.GetInt("offset", 0);
                var root = args.GetString("root");
                return OnUi(ctx, () => PageResult(ctx.Backend.FindPage(query, limit, offset, root)));
            });

        registry.Register(ToolCatalog.InspectElement,
            "Get detailed info for one element. Args: id, includeChildren=false, depth=1, properties=[].",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var includeChildren = args.GetBool("includeChildren", false);
                var depth = args.GetInt("depth", 1);
                var propertyNames = args.GetStringList("properties");
                return OnUi<object>(ctx, () =>
                {
                    var info = ctx.Backend.Inspect(id, includeChildren, depth, propertyNames);
                    if (info == null) throw StaleElement(id);
                    return info;
                });
            });

        registry.Register(ToolCatalog.WaitForElement,
            "Poll for the first matching element. Args: query, root(id optional), timeoutMs=10000, pollMs=200.",
            (ctx, args) =>
            {
                var query = args.GetRequiredString("query");
                var root = args.GetString("root");
                var timeoutMs = Math.Max(0, args.GetInt("timeoutMs", 10000));
                var pollMs = Math.Max(1, args.GetInt("pollMs", 200));
                var sw = Stopwatch.StartNew();

                while (true)
                {
                    var page = OnUi(ctx, () => ctx.Backend.FindPage(query, 1, 0, root));
                    if (page.Count > 0 || page.Elements.Count > 0)
                        return PageResult(page);

                    if (sw.ElapsedMilliseconds >= timeoutMs)
                    {
                        throw new PilotToolException(
                            PilotErrorCodes.Timeout,
                            $"Timed out waiting for element matching '{query}'.",
                            "Check the query/root or increase timeoutMs if the UI is still loading.");
                    }

                    Thread.Sleep(Math.Min(pollMs, Math.Max(1, timeoutMs - (int)sw.ElapsedMilliseconds)));
                }
            });

        registry.Register(ToolCatalog.Click,
            "Synthetically click an element (automation invoke / control click fallback). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.Click(id) });
            });

        registry.Register(ToolCatalog.Drag,
            "Drag with the real OS mouse (press, glide, release) so hit-testing, mouse capture and " +
            "Preview* handlers all run - use this for tiles, thumbs and drag/drop, which synthetic " +
            "click cannot reach. Start point: id (element centre) or fromX/fromY (screen px). " +
            "End point: toId (element centre), toX/toY (screen px), or dx/dy (offset from start). " +
            "Args: id, fromX, fromY, grabOffsetX, grabOffsetY, toId, toX, toY, dx, dy, " +
            "steps=24, stepDelayMs=12, settleMs=250.",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var toId = args.GetString("toId");
                var fromX = args.GetDouble("fromX");
                var fromY = args.GetDouble("fromY");
                var toX = args.GetDouble("toX");
                var toY = args.GetDouble("toY");
                var dx = args.GetDouble("dx");
                var dy = args.GetDouble("dy");
                var grabOffsetX = args.GetDouble("grabOffsetX") ?? 0;
                var grabOffsetY = args.GetDouble("grabOffsetY") ?? 0;
                var steps = args.GetInt("steps", 24);
                var stepDelayMs = args.GetInt("stepDelayMs", 12);
                var settleMs = args.GetInt("settleMs", 250);

                // Resolve geometry and raise the window on the UI thread, then inject from this
                // background pipe thread so the app can keep pumping while the drag runs.
                var route = OnUi(ctx, () =>
                {
                    ScreenPoint start;
                    if (id != null)
                        start = ctx.Backend.GetElementCentre(id);
                    else
                        start = new ScreenPoint(
                            fromX ?? throw new ArgumentException("Provide either 'id' or 'fromX'/'fromY'."),
                            fromY ?? throw new ArgumentException("Provide either 'id' or 'fromX'/'fromY'."));

                    start = new ScreenPoint(start.X + grabOffsetX, start.Y + grabOffsetY);

                    ScreenPoint end;
                    if (toId != null)
                        end = ctx.Backend.GetElementCentre(toId);
                    else if (toX.HasValue || toY.HasValue)
                        end = new ScreenPoint(toX ?? start.X, toY ?? start.Y);
                    else if (dx.HasValue || dy.HasValue)
                        end = new ScreenPoint(start.X + (dx ?? 0), start.Y + (dy ?? 0));
                    else
                        throw new ArgumentException("Provide a destination: 'toId', 'toX'/'toY', or 'dx'/'dy'.");

                    ctx.Backend.PrepareForRealInput(id);
                    return new { start, end };
                });

                try
                {
                    lock (DragGate)
                        RealInput.Drag(route.start, route.end, steps, stepDelayMs, settleMs);
                }
                catch (PlatformNotSupportedException ex)
                {
                    throw new PilotToolException(PilotErrorCodes.Platform, ex.Message);
                }

                return new
                {
                    from = new { x = route.start.X, y = route.start.Y },
                    to = new { x = route.end.X, y = route.end.Y },
                    steps
                };
            });

        registry.Register(ToolCatalog.TypeText,
            "Set text on a focusable input via automation Value pattern or text control. Args: id, text.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text") ?? string.Empty;
                return OnUi<object>(ctx, () => new { method = ctx.Backend.TypeText(id, text) });
            });

        registry.Register(ToolCatalog.PressKeys,
            "Send key chords (Ctrl+S, Enter, Tab, Escape) or sequential text. Args: id(optional), keys.",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var keys = args.GetRequiredString("keys");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.PressKeys(id, keys) });
            });

        registry.Register(ToolCatalog.Scroll,
            "Scroll an element by deltas. Args: id, dx=0, dy=0.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var dx = args.GetDouble("dx") ?? 0;
                var dy = args.GetDouble("dy") ?? 0;
                return OnUi<object>(ctx, () => new { method = ctx.Backend.Scroll(id, dx, dy) });
            });

        registry.Register(ToolCatalog.Focus,
            "Move keyboard focus to an element. Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.Focus(id) });
            });

        registry.Register(ToolCatalog.SelectItem,
            "Select an item by visible text or zero-based index. Args: id, text(optional), index(optional).",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text");
                var index = GetNullableInt(args, "index");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.SelectItem(id, text, index) });
            });

        registry.Register(ToolCatalog.InvokeCommand,
            "Execute the ICommand bound to an element (e.g. Button.Command). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return OnUi<object>(ctx, () => new { result = ctx.Backend.InvokeCommand(id) });
            });

        registry.Register(ToolCatalog.Screenshot,
            "Capture a PNG of a window (default main window) or a specific element. Args: id (optional).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return OnUi<object>(ctx, () =>
                {
                    var shot = ctx.Backend.Screenshot(id);
                    if (shot == null) throw new InvalidOperationException("Nothing renderable to capture.");
                    return shot;
                });
            });

        registry.Register(ToolCatalog.SetWindowState,
            "Minimize/restore/maximize a window and optionally bring it to the foreground. Screenshots still work while minimized. Args: id (optional, defaults to main window), state (minimized|normal|maximized), activate=false.",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var state = args.GetString("state") ?? "normal";
                var activate = args.GetBool("activate", false);
                return OnUi<object>(ctx, () => new { state = ctx.Backend.SetWindowState(id, state, activate) });
            });

        registry.Register(ToolCatalog.BringToFront,
            "Restore (if minimized) and pull a window to the foreground so a human can see it. Args: id (optional, defaults to main window).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return OnUi<object>(ctx, () => new { state = ctx.Backend.BringToFront(id) });
            });

        registry.Register(ToolCatalog.GetBindingErrors,
            "Return captured data-binding errors/warnings. Args: clear=false.",
            (ctx, args) =>
            {
                var clear = args.GetBool("clear", false);
                var errors = ctx.Backend.GetBindingErrors();
                if (clear) ctx.Backend.ClearBindingErrors();
                return new { count = errors.Count, errors };
            });

        registry.Register(ToolCatalog.AnalyzeLayout,
            "Flag zero-size and off-screen visible elements. Args: root (id, optional).",
            (ctx, args) =>
            {
                var root = args.GetString("root");
                return OnUi(ctx, () =>
                {
                    var issues = ctx.Backend.AnalyzeLayout(root);
                    return new { count = issues.Count, issues };
                });
            });

        registry.Register(ToolCatalog.HighlightElement,
            "Briefly draw a red overlay over an element. Args: id, durationMs=1500.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var durationMs = args.GetInt("durationMs", 1500);
                return OnUi<object>(ctx, () => new { highlighted = ctx.Backend.Highlight(id, durationMs) });
            });
    }

    private static object PageResult(FindPage page) => new
    {
        count = page.Count,
        hasMore = page.HasMore,
        offset = page.Offset,
        limit = page.Limit,
        elements = page.Elements,
    };

    private static T OnUi<T>(ToolContext ctx, Func<T> func)
    {
        try
        {
            return ctx.OnUi(func);
        }
        catch (ArgumentException ex) when (IsStaleElement(ex))
        {
            throw StaleElement(ex.Message);
        }
    }

    private static bool IsStaleElement(ArgumentException ex) =>
        ex.Message.IndexOf("Unknown or collected element", StringComparison.OrdinalIgnoreCase) >= 0;

    private static PilotToolException StaleElement(string idOrMessage)
    {
        var message = idOrMessage.IndexOf("Unknown or collected element", StringComparison.OrdinalIgnoreCase) >= 0
            ? idOrMessage
            : $"Unknown or collected element '{idOrMessage}'.";
        return new PilotToolException(
            PilotErrorCodes.StaleElement,
            message,
            "Refresh element handles with find_elements or inspect_element before retrying.");
    }

    private static int? GetNullableInt(System.Text.Json.JsonElement args, string name)
    {
        if (args.ValueKind == System.Text.Json.JsonValueKind.Object &&
            args.TryGetProperty(name, out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.Number &&
            v.TryGetInt32(out var i))
            return i;
        return null;
    }
}
