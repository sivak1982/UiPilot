using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UiPilot.Tools;

namespace UiPilot.WinForms.Interaction;

/// <summary>WinForms control and ToolStrip interaction helpers. UI-thread only.</summary>
internal static class SyntheticInput
{
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    public static string Click(object obj)
    {
        switch (obj)
        {
            case ToolStripDropDownItem dropDown when dropDown.HasDropDownItems:
                dropDown.ShowDropDown();
                return "synthetic:toolstrip-expand";
            case ToolStripItem item:
                item.PerformClick();
                return "synthetic:toolstrip-click";
            case RadioButton radio:
                radio.Checked = true;
                radio.PerformClick();
                return "synthetic:radio-select";
            case CheckBox check:
                check.Checked = !check.Checked;
                InvokeProtectedOnClick(check);
                return "synthetic:toggle";
            case Button button:
                button.PerformClick();
                return "synthetic:perform-click";
            case Control control:
                control.Focus();
                InvokeProtectedOnClick(control);
                return "synthetic:on-click";
            default:
                throw new InvalidOperationException($"Element of type '{obj.GetType().Name}' does not support click.");
        }
    }

    public static string TypeText(object obj, string text)
    {
        if (obj is not Control control)
            throw new InvalidOperationException("Target is not a Control.");
        control.Focus();
        var value = text ?? string.Empty;
        switch (control)
        {
            case TextBoxBase textBox:
                textBox.Text = value;
                textBox.SelectionStart = textBox.TextLength;
                return "synthetic:textbox-set";
            case ComboBox combo:
                combo.Text = value;
                return "synthetic:combobox-set";
            default:
                control.Text = value;
                return "synthetic:text-set";
        }
    }

    public static string PressKeys(object? obj, string keys)
    {
        if (string.IsNullOrEmpty(keys))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Keys cannot be empty.");
        if (obj is Control control)
            control.Focus();
        else if (obj is ToolStripItem item)
            item.Select();
        else if (obj != null)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a Control or ToolStripItem.");

        SendKeys.SendWait(NormalizeKeys(keys));
        return "synthetic:sendkeys";
    }

    public static string Scroll(object obj, double dx, double dy)
    {
        var control = obj as Control ?? (obj as ToolStripItem)?.Owner;
        if (control == null || !control.IsHandleCreated)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target has no window handle.");
        if (dy != 0)
            SendMessage(control.Handle, WmMouseWheel, WheelWParam(dy), IntPtr.Zero);
        if (dx != 0)
            SendMessage(control.Handle, WmMouseHWheel, WheelWParam(dx), IntPtr.Zero);
        return "synthetic:scroll";
    }

    public static string Focus(object obj)
    {
        if (obj is Control control)
        {
            control.Focus();
            return "synthetic:focus";
        }
        if (obj is ToolStripItem item)
        {
            item.Select();
            return "synthetic:toolstrip-focus";
        }
        throw new InvalidOperationException("Target cannot receive focus.");
    }

    public static string SelectItem(object obj, string? text, int? index)
    {
        if (index == null && string.IsNullOrWhiteSpace(text))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide either 'index' or 'text'.");

        if (obj is ToolStripDropDownItem menu)
            return SelectToolStripItem(menu, text, index);
        if (obj is TabControl tabs)
            return SelectTab(tabs, text, index);
        if (obj is ComboBox combo)
            return SelectList(combo.Items, i => combo.SelectedIndex = i, text, index);
        if (obj is ListBox list)
            return SelectList(list.Items, i => list.SelectedIndex = i, text, index);
        if (obj is ListView view)
            return SelectListView(view, text, index);

        throw new PilotToolException(
            PilotErrorCodes.Unsupported, $"Element of type '{obj.GetType().Name}' does not support item selection.");
    }

    private static string SelectToolStripItem(ToolStripDropDownItem menu, string? text, int? index)
    {
        menu.ShowDropDown();
        var selected = ResolveIndex(menu.DropDownItems.Count, i => menu.DropDownItems[i].Text, text, index);
        menu.DropDownItems[selected].PerformClick();
        return index.HasValue ? "synthetic:select-index" : "synthetic:select-text";
    }

    private static string SelectTab(TabControl tabs, string? text, int? index)
    {
        var selected = ResolveIndex(tabs.TabPages.Count, i => tabs.TabPages[i].Text, text, index);
        tabs.SelectedIndex = selected;
        return index.HasValue ? "synthetic:select-index" : "synthetic:select-text";
    }

    private static string SelectList(
        System.Collections.IList items, Action<int> select, string? text, int? index)
    {
        var selected = ResolveIndex(items.Count, i => items[i]?.ToString(), text, index);
        select(selected);
        return index.HasValue ? "synthetic:select-index" : "synthetic:select-text";
    }

    private static string SelectListView(ListView view, string? text, int? index)
    {
        var selected = ResolveIndex(view.Items.Count, i => view.Items[i].Text, text, index);
        view.Items[selected].Selected = true;
        view.Items[selected].Focused = true;
        return index.HasValue ? "synthetic:select-index" : "synthetic:select-text";
    }

    private static int ResolveIndex(int count, Func<int, string?> textAt, string? text, int? index)
    {
        if (index.HasValue)
        {
            if (index.Value < 0 || index.Value >= count)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Selection index {index.Value} is out of range.");
            return index.Value;
        }
        for (var i = 0; i < count; i++)
            if (string.Equals(textAt(i)?.Trim(), text!.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        throw new PilotToolException(PilotErrorCodes.NotFound, $"No selectable item matching '{text}' was found.");
    }

    private static string NormalizeKeys(string keys)
    {
        if (!keys.Contains("+", StringComparison.Ordinal))
            return SpecialKey(keys) ?? keys;
        var parts = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = "";
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers += parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => "^",
                "ALT" => "%",
                "SHIFT" => "+",
                _ => throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown modifier '{parts[i]}'."),
            };
        }
        return modifiers + (SpecialKey(parts[^1]) ?? parts[^1]);
    }

    private static string? SpecialKey(string key) => key.Trim().ToUpperInvariant() switch
    {
        "ENTER" or "RETURN" => "{ENTER}",
        "TAB" => "{TAB}",
        "ESC" or "ESCAPE" => "{ESC}",
        "BACKSPACE" or "BKSP" => "{BACKSPACE}",
        "DELETE" or "DEL" => "{DELETE}",
        "LEFT" => "{LEFT}",
        "RIGHT" => "{RIGHT}",
        "UP" => "{UP}",
        "DOWN" => "{DOWN}",
        "HOME" => "{HOME}",
        "END" => "{END}",
        "PAGEUP" or "PGUP" => "{PGUP}",
        "PAGEDOWN" or "PGDN" => "{PGDN}",
        "SPACE" => " ",
        var value when value.Length is 2 or 3 && value[0] == 'F' && int.TryParse(value[1..], out var fn) && fn is >= 1 and <= 12
            => "{" + value + "}",
        _ => null,
    };

    private static void InvokeProtectedOnClick(Control control)
    {
        var method = typeof(Control).GetMethod(
            "OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(control, new object[] { EventArgs.Empty });
    }

    private static IntPtr WheelWParam(double delta)
    {
        var amount = Math.Abs(delta) < 1 ? Math.Sign(delta) * 120 : (int)Math.Round(delta);
        return (IntPtr)(amount << 16);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
