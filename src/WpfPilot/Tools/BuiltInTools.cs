using System;
using System.Windows;
using WpfPilot.Inspection;
using WpfPilot.Interaction;
using WpfPilot.Media;

namespace WpfPilot.Tools;

/// <summary>Registers the ~10 v1 built-in tools. Every UI touch is marshaled to the dispatcher.</summary>
internal static class BuiltInTools
{
    public static void RegisterAll(ToolRegistry registry)
    {
        registry.Register("list_windows",
            "List all top-level windows with identity and bounds.",
            (ctx, _) => ctx.OnUi(() => new { windows = VisualTreeQuery.ListWindows(ctx.Elements) }));

        registry.Register("find_elements",
            "Search the visual tree by name, AutomationId, type, or text. Args: query, limit=50, root(id).",
            (ctx, args) =>
            {
                var query = args.GetString("query");
                var limit = args.GetInt("limit", 50);
                var root = args.GetString("root");
                return ctx.OnUi(() =>
                {
                    var elements = VisualTreeQuery.Find(ctx.Elements, query, limit, root);
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
                    var info = VisualTreeQuery.Inspect(ctx.Elements, id, includeChildren, depth);
                    if (info == null) throw new ArgumentException($"Unknown or collected element '{id}'.");
                    return info;
                });
            });

        registry.Register("click",
            "Synthetically click an element (automation invoke, then ButtonBase fallback). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return ctx.OnUi<object>(() =>
                {
                    var obj = Require(ctx, id);
                    return new { method = SyntheticInput.Click(obj) };
                });
            });

        registry.Register("type_text",
            "Set text on a focusable input via automation Value pattern or TextBox. Args: id, text.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var text = args.GetString("text") ?? string.Empty;
                return ctx.OnUi<object>(() =>
                {
                    var obj = Require(ctx, id);
                    return new { method = SyntheticInput.TypeText(obj, text) };
                });
            });

        registry.Register("invoke_command",
            "Execute the ICommand bound to an element (e.g. Button.Command). Args: id.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                return ctx.OnUi<object>(() =>
                {
                    var obj = Require(ctx, id);
                    return new { result = SyntheticInput.InvokeCommand(obj) };
                });
            });

        registry.Register("screenshot",
            "Capture a PNG of a window (default main window) or a specific element. Args: id (optional).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return ctx.OnUi<object>(() =>
                {
                    var target = id == null ? null : Require(ctx, id);
                    var shot = Screenshot.Capture(target);
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
                return ctx.OnUi<object>(() =>
                {
                    var target = id == null ? null : Require(ctx, id);
                    var window = WindowControl.ResolveWindow(target)
                        ?? throw new InvalidOperationException("No window to control.");
                    return new { state = WindowControl.SetState(window, state, activate) };
                });
            });

        registry.Register("bring_to_front",
            "Restore (if minimized) and pull a window to the foreground so a human can see it. Args: id (optional, defaults to main window).",
            (ctx, args) =>
            {
                var id = args.GetString("id");
                return ctx.OnUi<object>(() =>
                {
                    var target = id == null ? null : Require(ctx, id);
                    var window = WindowControl.ResolveWindow(target)
                        ?? throw new InvalidOperationException("No window to bring to front.");
                    return new { state = WindowControl.Foreground(window) };
                });
            });

        registry.Register("get_binding_errors",
            "Return captured WPF data-binding errors/warnings. Args: clear=false.",
            (ctx, args) =>
            {
                var clear = args.GetBool("clear", false);
                var errors = ctx.Bindings.Snapshot();
                if (clear) ctx.Bindings.Clear();
                return new { count = errors.Count, errors };
            });

        registry.Register("analyze_layout",
            "Flag zero-size and off-screen visible elements. Args: root (id, optional).",
            (ctx, args) =>
            {
                var root = args.GetString("root");
                return ctx.OnUi(() =>
                {
                    var issues = LayoutAnalyzer.Analyze(ctx.Elements, root);
                    return new { count = issues.Count, issues };
                });
            });

        registry.Register("highlight_element",
            "Briefly draw a red overlay over an element. Args: id, durationMs=1500.",
            (ctx, args) =>
            {
                var id = args.GetRequiredString("id");
                var durationMs = args.GetInt("durationMs", 1500);
                return ctx.OnUi<object>(() =>
                {
                    var obj = Require(ctx, id);
                    return new { highlighted = HighlightOverlay.Highlight(obj, durationMs) };
                });
            });
    }

    private static DependencyObject Require(ToolContext ctx, string id)
    {
        var obj = ctx.Elements.Resolve(id);
        if (obj == null) throw new ArgumentException($"Unknown or collected element '{id}'.");
        return obj;
    }
}
