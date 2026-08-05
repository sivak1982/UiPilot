using System.Collections.Generic;

namespace WpfPilot.Tools;

/// <summary>Stable public catalog of built-in tool names for tests, CLIs, and docs parity.</summary>
public static class ToolCatalog
{
    public const string ListWindows = "list_windows";
    public const string FindElements = "find_elements";
    public const string InspectElement = "inspect_element";
    public const string WaitForElement = "wait_for_element";
    public const string Click = "click";
    public const string Drag = "drag";
    public const string TypeText = "type_text";
    public const string PressKeys = "press_keys";
    public const string Scroll = "scroll";
    public const string Focus = "focus";
    public const string SelectItem = "select_item";
    public const string InvokeCommand = "invoke_command";
    public const string Screenshot = "screenshot";
    public const string SetWindowState = "set_window_state";
    public const string BringToFront = "bring_to_front";
    public const string GetBindingErrors = "get_binding_errors";
    public const string AnalyzeLayout = "analyze_layout";
    public const string HighlightElement = "highlight_element";

    public static IReadOnlyList<string> BuiltInToolNames { get; } = new[]
    {
        ListWindows,
        FindElements,
        InspectElement,
        WaitForElement,
        Click,
        Drag,
        TypeText,
        PressKeys,
        Scroll,
        Focus,
        SelectItem,
        InvokeCommand,
        Screenshot,
        SetWindowState,
        BringToFront,
        GetBindingErrors,
        AnalyzeLayout,
        HighlightElement,
    };
}
