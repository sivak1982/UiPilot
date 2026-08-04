using System;
using WpfPilot.Abstraction;
using WpfPilot.Interaction;

namespace WpfPilot.Tools;

/// <summary>
/// Registers the built-in agent tools against an <see cref="IUiBackend"/>. Tool names and
/// argument contracts are identical for WPF and Avalonia.
/// </summary>
internal static class BuiltInTools
{
    public static void RegisterAll(ToolRegistry registry)
    {
        registry.Register("list_windows",
            "List all top-level windows with identity and bounds.",
            (ctx, _) => ctx.OnUi(() => new { windows = ctx.Backend.ListWindows() }));

        registry.Register("find_elements",
            "Search the visual tree by name, AutomationId, type, or text. Args: query, limit=50, root(id).",
            (ctx, args) =>
            {
                var query = args.GetString("query");
                var limit = args.GetInt("limit", 50);
                var root = args.GetString("root");
                return ctx.OnUi(() =>
                {
                    var elements = ctx.Backend.Find(query, limit, root);
                    return new { count = elements.Count, elements };
                });
            });

        registry.Register("inspect_element",
            "Get detailed info for one element. Args: id, includeChildren=false, depth=1.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var includeChildren = args.GetBool("includeChildren", false);
                var depth = args.GetInt("depth", 1);
                return ctx.OnUi<object>(() =>
                {
                    var info = ctx.Backend.Inspect(id, includeChildren, depth);
                    if (info == null) throw new ArgumentException($"Unknown or collected element '{id}'.");
                    return info;
                });
            });

        registry.Register("click",
            "Synthetically click an element (automation invoke / control click fallback). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return ctx.OnUi<object>(() => new { method = ctx.Backend.Click(id) });
            });

        registry.Register("drag",
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
                var route = ctx.OnUi(() =>
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

                RealInput.Drag(route.start, route.end, steps, stepDelayMs, settleMs);

                return new
                {
                    from = new { x = route.start.X, y = route.start.Y },
                    to = new { x = route.end.X, y = route.end.Y },
                    steps
                };
            });

        registry.Register("type_text",
            "Set text on a focusable input via automation Value pattern or text control. Args: id, text.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text") ?? string.Empty;
                return ctx.OnUi<object>(() => new { method = ctx.Backend.TypeText(id, text) });
            });

        registry.Register("invoke_command",
            "Execute the ICommand bound to an element (e.g. Button.Command). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return ctx.OnUi<object>(() => new { result = ctx.Backend.InvokeCommand(id) });
            });

        registry.Register("screenshot",
            "Capture a PNG of a window (default main window) or a specific element. Args: id (optional).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return ctx.OnUi<object>(() =>
                {
                    var shot = ctx.Backend.Screenshot(id);
                    if (shot == null) throw new InvalidOperationException("Nothing renderable to capture.");
                    return shot;
                });
            });

        registry.Register("set_window_state",
            "Minimize/restore/maximize a window and optionally bring it to the foreground. Screenshots still work while minimized. Args: id (optional, defaults to main window), state (minimized|normal|maximized), activate=false.",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                var state = args.GetString("state") ?? "normal";
                var activate = args.GetBool("activate", false);
                return ctx.OnUi<object>(() => new { state = ctx.Backend.SetWindowState(id, state, activate) });
            });

        registry.Register("bring_to_front",
            "Restore (if minimized) and pull a window to the foreground so a human can see it. Args: id (optional, defaults to main window).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return ctx.OnUi<object>(() => new { state = ctx.Backend.BringToFront(id) });
            });

        registry.Register("get_binding_errors",
            "Return captured data-binding errors/warnings. Args: clear=false.",
            (ctx, args) =>
            {
                var clear = args.GetBool("clear", false);
                var errors = ctx.Backend.GetBindingErrors();
                if (clear) ctx.Backend.ClearBindingErrors();
                return new { count = errors.Count, errors };
            });

        registry.Register("analyze_layout",
            "Flag zero-size and off-screen visible elements. Args: root (id, optional).",
            (ctx, args) =>
            {
                var root = args.GetString("root");
                return ctx.OnUi(() =>
                {
                    var issues = ctx.Backend.AnalyzeLayout(root);
                    return new { count = issues.Count, issues };
                });
            });

        registry.Register("highlight_element",
            "Briefly draw a red overlay over an element. Args: id, durationMs=1500.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var durationMs = args.GetInt("durationMs", 1500);
                return ctx.OnUi<object>(() => new { highlighted = ctx.Backend.Highlight(id, durationMs) });
            });
    }
}
