using System;
using System.Diagnostics;
using System.Threading;
using UiPilot.Abstraction;
using UiPilot.Interaction;

namespace UiPilot.Tools;

/// <summary>
/// Registers the built-in agent tools against an <see cref="IUiBackend"/>. Tool names and
/// argument contracts are identical across WPF, Avalonia, and WinForms.
/// </summary>
internal static class BuiltInTools
{
    private static readonly object DragGate = new object();

    public static void RegisterAll(ToolRegistry registry)
    {
        registry.Register(ToolCatalog.ListWindows,
            "List all top-level windows with identity and bounds.",
            ToolSchemas.EmptyObject,
            (ctx, _) => ctx.OnUi(() => new { windows = ctx.Backend.ListWindows() }));

        registry.Register(ToolCatalog.FindElements,
            "Search the visual tree by name, AutomationId, type, or text. "
            + "Args: query, limit=50, offset=0, root(id), exact=false.",
            ToolSchemas.Object(new
            {
                query = ToolSchemas.String("Name, AutomationId, type, or text substring."),
                limit = ToolSchemas.Integer("Max results (default 50)."),
                offset = ToolSchemas.Integer("Skip this many matches (default 0)."),
                root = ToolSchemas.String("Optional element id to search under."),
                exact = ToolSchemas.Boolean("Require whole-value equality (default false)."),
            }),
            (ctx, args) =>
            {
                var query = args.GetString("query");
                var limit = args.GetInt("limit", 50);
                var offset = args.GetInt("offset", 0);
                var root = args.GetString("root");
                var exact = args.GetBool("exact", false);
                return OnUi(ctx, () => PageResult(ctx.Backend.FindPage(query, limit, offset, root, exact)));
            });

        registry.Register(ToolCatalog.InspectElement,
            "Get detailed info for one element. Args: id, includeChildren=false, depth=1, properties=[].",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String("Element handle from find_elements / inspect."),
                includeChildren = ToolSchemas.Boolean(),
                depth = ToolSchemas.Integer(),
                properties = ToolSchemas.StringArray("Optional property names to read."),
            }, required: new[] { "id" }),
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

        registry.Register(ToolCatalog.FindAncestor,
            "Walk up from an element to the nearest ancestor of a given type. "
            + "Args: id, type(optional), maxDepth=25.",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                type = ToolSchemas.String("Ancestor CLR type name (optional)."),
                maxDepth = ToolSchemas.Integer(),
            }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var type = args.GetString("type");
                var maxDepth = Math.Max(1, args.GetInt("maxDepth", 25));
                return OnUi<object>(ctx, () =>
                {
                    var info = ctx.Backend.FindAncestor(id, type, maxDepth);
                    if (info == null)
                    {
                        throw new PilotToolException(
                            PilotErrorCodes.NotFound,
                            $"No ancestor of type '{type}' within {maxDepth} levels of '{id}'.",
                            "Inspect the element with includeChildren to confirm the type name, "
                            + "or raise maxDepth.");
                    }

                    return info;
                });
            });

        registry.Register(ToolCatalog.WaitForElement,
            "Poll for the first matching element. "
            + "Args: query, root(id optional), timeoutMs=10000, pollMs=200, exact=false.",
            ToolSchemas.Object(new
            {
                query = ToolSchemas.String(),
                root = ToolSchemas.String(),
                timeoutMs = ToolSchemas.Integer(),
                pollMs = ToolSchemas.Integer(),
                exact = ToolSchemas.Boolean(),
            }, required: new[] { "query" }),
            (ctx, args) =>
            {
                var query = args.GetRequiredString("query");
                var root = args.GetString("root");
                var timeoutMs = Math.Max(0, args.GetInt("timeoutMs", 10000));
                var pollMs = Math.Max(1, args.GetInt("pollMs", 200));
                var exact = args.GetBool("exact", false);
                var ct = ctx.CancellationToken;
                var sw = Stopwatch.StartNew();

                try
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        var page = OnUi(ctx, () => ctx.Backend.FindPage(query, 1, 0, root, exact));
                        if (page.Total > 0 || page.Elements.Count > 0)
                            return PageResult(page);

                        if (sw.ElapsedMilliseconds >= timeoutMs)
                        {
                            throw new PilotToolException(
                                PilotErrorCodes.Timeout,
                                $"Timed out waiting for element matching '{query}'.",
                                "Check the query/root or increase timeoutMs if the UI is still loading.");
                        }

                        var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
                        WaitCancelable(Math.Min(pollMs, Math.Max(1, remaining)), ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new PilotToolException(PilotErrorCodes.Canceled, "wait_for_element was canceled.");
                }
            });

        registry.Register(ToolCatalog.Click,
            "Synthetically click an element (automation invoke / control click fallback). Args: id.",
            ToolSchemas.Object(new { id = ToolSchemas.String() }, required: new[] { "id" }),
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
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                fromX = ToolSchemas.Number(),
                fromY = ToolSchemas.Number(),
                grabOffsetX = ToolSchemas.Number(),
                grabOffsetY = ToolSchemas.Number(),
                toId = ToolSchemas.String(),
                toX = ToolSchemas.Number(),
                toY = ToolSchemas.Number(),
                dx = ToolSchemas.Number(),
                dy = ToolSchemas.Number(),
                steps = ToolSchemas.Integer(),
                stepDelayMs = ToolSchemas.Integer(),
                settleMs = ToolSchemas.Integer(),
            }),
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
                var steps = Clamp(args.GetInt("steps", 24), 1, 500);
                var stepDelayMs = Clamp(args.GetInt("stepDelayMs", 12), 0, 1000);
                var settleMs = Clamp(args.GetInt("settleMs", 250), 0, 10_000);
                var ct = ctx.CancellationToken;

                var route = OnUi(ctx, () =>
                {
                    ScreenPoint start;
                    if (id != null)
                        start = ctx.Backend.GetElementCentre(id);
                    else
                        start = new ScreenPoint(
                            fromX ?? throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide either 'id' or 'fromX'/'fromY'."),
                            fromY ?? throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide either 'id' or 'fromX'/'fromY'."));

                    start = new ScreenPoint(start.X + grabOffsetX, start.Y + grabOffsetY);

                    ScreenPoint end;
                    if (toId != null)
                        end = ctx.Backend.GetElementCentre(toId);
                    else if (toX.HasValue || toY.HasValue)
                        end = new ScreenPoint(toX ?? start.X, toY ?? start.Y);
                    else if (dx.HasValue || dy.HasValue)
                        end = new ScreenPoint(start.X + (dx ?? 0), start.Y + (dy ?? 0));
                    else
                        throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide a destination: 'toId', 'toX'/'toY', or 'dx'/'dy'.");

                    ctx.Backend.PrepareForRealInput(id);
                    return new { start, end };
                });

                try
                {
                    lock (DragGate)
                        RealInput.Drag(route.start, route.end, steps, stepDelayMs, settleMs, ct);
                }
                catch (OperationCanceledException)
                {
                    throw new PilotToolException(PilotErrorCodes.Canceled, "drag was canceled.");
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
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                text = ToolSchemas.String(),
            }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text") ?? string.Empty;
                return OnUi<object>(ctx, () => new { method = ctx.Backend.TypeText(id, text) });
            });

        registry.Register(ToolCatalog.PressKeys,
            "Send key chords (Ctrl+S, Enter, Tab, Escape) or sequential text. Args: id(optional), keys.",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                keys = ToolSchemas.String(),
            }, required: new[] { "keys" }),
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var keys = args.GetRequiredString("keys");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.PressKeys(id, keys) });
            });

        registry.Register(ToolCatalog.Scroll,
            "Scroll an element by line deltas (1 ~= one mouse-wheel notch). Args: id, dx=0, dy=0.",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                dx = ToolSchemas.Number("Horizontal scroll lines."),
                dy = ToolSchemas.Number("Vertical scroll lines."),
            }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var dx = args.GetDouble("dx") ?? 0;
                var dy = args.GetDouble("dy") ?? 0;
                return OnUi<object>(ctx, () => new { method = ctx.Backend.Scroll(id, dx, dy) });
            });

        registry.Register(ToolCatalog.Focus,
            "Move keyboard focus to an element. Args: id.",
            ToolSchemas.Object(new { id = ToolSchemas.String() }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.Focus(id) });
            });

        registry.Register(ToolCatalog.SelectItem,
            "Select an item by visible text or zero-based index. Args: id, text(optional), index(optional).",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                text = ToolSchemas.String(),
                index = ToolSchemas.Integer(),
            }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text");
                var index = GetNullableInt(args, "index");
                return OnUi<object>(ctx, () => new { method = ctx.Backend.SelectItem(id, text, index) });
            });

        registry.Register(ToolCatalog.InvokeCommand,
            "Execute the ICommand bound to an element (e.g. Button.Command). Args: id.",
            ToolSchemas.Object(new { id = ToolSchemas.String() }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return OnUi<object>(ctx, () => new { result = ctx.Backend.InvokeCommand(id) });
            });

        registry.Register(ToolCatalog.Screenshot,
            "Capture a PNG of a window (default main window) or a specific element. Args: id (optional).",
            ToolSchemas.Object(new { id = ToolSchemas.String() }),
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return OnUi<object>(ctx, () =>
                {
                    var shot = ctx.Backend.Screenshot(id);
                    if (shot == null)
                    {
                        throw new PilotToolException(
                            PilotErrorCodes.NotFound,
                            "Nothing renderable to capture.",
                            "Pass a window/element id, or ensure the main window exists.");
                    }
                    return shot;
                });
            });

        registry.Register(ToolCatalog.SetWindowState,
            "Minimize/restore/maximize a window and optionally bring it to the foreground. Screenshots still work while minimized. Args: id (optional, defaults to main window), state (minimized|normal|maximized), activate=false.",
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                state = ToolSchemas.String("minimized|normal|maximized"),
                activate = ToolSchemas.Boolean(),
            }),
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var state = args.GetString("state") ?? "normal";
                var activate = args.GetBool("activate", false);
                return OnUi<object>(ctx, () => new { state = ctx.Backend.SetWindowState(id, state, activate) });
            });

        registry.Register(ToolCatalog.ResizeWindow,
            "Restore a window to normal (if needed) and set its size. Optionally move it and/or activate. "
            + "Args: width, height, id (optional), x (optional), y (optional), activate=false.",
            ToolSchemas.Object(new
            {
                width = ToolSchemas.Number(),
                height = ToolSchemas.Number(),
                id = ToolSchemas.String(),
                x = ToolSchemas.Number(),
                y = ToolSchemas.Number(),
                activate = ToolSchemas.Boolean(),
            }, required: new[] { "width", "height" }),
            (ctx, args) =>
            {
                var width = RequirePositiveSize(args, "width");
                var height = RequirePositiveSize(args, "height");
                var id = args.GetString("id");
                var x = args.GetDouble("x");
                var y = args.GetDouble("y");
                var activate = args.GetBool("activate", false);
                return OnUi<object>(ctx, () => ctx.Backend.ResizeWindow(id, width, height, x, y, activate));
            });

        registry.Register(ToolCatalog.BringToFront,
            "Restore (if minimized) and pull a window to the foreground so a human can see it. Args: id (optional, defaults to main window).",
            ToolSchemas.Object(new { id = ToolSchemas.String() }),
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return OnUi<object>(ctx, () => new { state = ctx.Backend.BringToFront(id) });
            });

        registry.Register(ToolCatalog.GetBindingErrors,
            "Return captured data-binding errors/warnings. Args: clear=false.",
            ToolSchemas.Object(new { clear = ToolSchemas.Boolean() }),
            (ctx, args) =>
            {
                var clear = args.GetBool("clear", false);
                return OnUi(ctx, () =>
                {
                    var errors = ctx.Backend.GetBindingErrors();
                    if (clear) ctx.Backend.ClearBindingErrors();
                    return new { count = errors.Count, errors };
                });
            });

        registry.Register(ToolCatalog.AnalyzeLayout,
            "Flag zero-size and off-screen visible elements. Args: root (id, optional).",
            ToolSchemas.Object(new { root = ToolSchemas.String() }),
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
            ToolSchemas.Object(new
            {
                id = ToolSchemas.String(),
                durationMs = ToolSchemas.Integer(),
            }, required: new[] { "id" }),
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var durationMs = Clamp(args.GetInt("durationMs", 1500), 0, 30_000);
                return OnUi<object>(ctx, () => new { highlighted = ctx.Backend.Highlight(id, durationMs) });
            });
    }

    private static object PageResult(FindPage page) => new
    {
        count = page.Count,
        total = page.Total,
        hasMore = page.HasMore,
        offset = page.Offset,
        limit = page.Limit,
        elements = page.Elements,
    };

    private static void WaitCancelable(int milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0)
            return;

        if (ct.WaitHandle.WaitOne(milliseconds))
            ct.ThrowIfCancellationRequested();
    }

    private static T OnUi<T>(ToolContext ctx, Func<T> func)
    {
        try
        {
            return ctx.OnUi(func);
        }
        catch (PilotToolException ex) when (ex.Code == PilotErrorCodes.StaleElement)
        {
            throw;
        }
    }

    private static PilotToolException StaleElement(string id) =>
        new(
            PilotErrorCodes.StaleElement,
            $"Unknown or collected element '{id}'.",
            "Refresh element handles with find_elements or inspect_element before retrying.");

    private static int? GetNullableInt(System.Text.Json.JsonElement args, string name)
    {
        if (args.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !args.TryGetProperty(name, out var v) ||
            v.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
            return null;
        if (v.ValueKind != System.Text.Json.JsonValueKind.Number || !v.TryGetInt32(out var i))
        {
            throw new PilotToolException(
                PilotErrorCodes.InvalidArgs,
                $"Argument '{name}' must be an integer (got {v.ValueKind}).");
        }
        return i;
    }

    private static double RequirePositiveSize(System.Text.Json.JsonElement args, string name)
    {
        var value = args.GetDouble(name);
        if (value == null)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Missing required number argument '{name}'.");
        if (value.Value <= 0 || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Argument '{name}' must be a positive number.");
        return value.Value;
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : (value > max ? max : value);
}
