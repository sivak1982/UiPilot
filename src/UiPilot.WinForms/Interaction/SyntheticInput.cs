using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UiPilot.Tools;
using UiPilot.Interaction;

namespace UiPilot.WinForms.Interaction;

/// <summary>WinForms control and ToolStrip interaction helpers. UI-thread only.</summary>
internal static class SyntheticInput
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmChar = 0x0102;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
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
                throw new PilotToolException(
                    PilotErrorCodes.Unsupported,
                    $"Element of type '{control.GetType().Name}' does not support text entry.");
        }
    }

    public static string PressKeys(object? obj, string keys)
    {
        if (string.IsNullOrEmpty(keys))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Keys cannot be empty.");

        var control = ResolveControl(obj);
        if (!control.IsHandleCreated)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target has no window handle.");
        control.Focus();

        var stroke = KeyStroke.Parse(keys);
        PostKeyStroke(control.Handle, stroke);
        return "synthetic:postmessage-keys";
    }

    public static string Scroll(object obj, double dx, double dy)
    {
        var control = obj as Control ?? (obj as ToolStripItem)?.Owner;
        if (control == null || !control.IsHandleCreated)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target has no window handle.");
        if (dy != 0)
            SendMessage(control.Handle, WmMouseWheel, WheelWParam(ScrollMetrics.ToWheelDelta(dy)), IntPtr.Zero);
        if (dx != 0)
            SendMessage(control.Handle, WmMouseHWheel, WheelWParam(ScrollMetrics.ToWheelDelta(dx)), IntPtr.Zero);
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

    private static Control ResolveControl(object? obj)
    {
        if (obj is Control control)
            return control;
        if (obj is ToolStripItem item)
            return item.Owner
                ?? throw new PilotToolException(PilotErrorCodes.InvalidArgs, "ToolStripItem has no owner control.");
        if (obj != null)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a Control or ToolStripItem.");
        if (Form.ActiveForm is { } active)
            return active.ActiveControl ?? active;
        throw new PilotToolException(PilotErrorCodes.InvalidArgs, "No target control or focused WinForms control is available.");
    }

    private static void PostKeyStroke(IntPtr hwnd, KeyStroke stroke)
    {
        foreach (var modifier in stroke.ModifierVks)
            PostKey(hwnd, modifier, down: true, system: stroke.IsAlt);

        if (stroke.Char is char ch)
        {
            PostKey(hwnd, stroke.VirtualKey, down: true, system: stroke.IsAlt);
            PostMessage(hwnd, WmChar, (IntPtr)ch, IntPtr.Zero);
            PostKey(hwnd, stroke.VirtualKey, down: false, system: stroke.IsAlt);
        }
        else
        {
            PostKey(hwnd, stroke.VirtualKey, down: true, system: stroke.IsAlt);
            PostKey(hwnd, stroke.VirtualKey, down: false, system: stroke.IsAlt);
        }

        for (var i = stroke.ModifierVks.Count - 1; i >= 0; i--)
            PostKey(hwnd, stroke.ModifierVks[i], down: false, system: stroke.IsAlt);
    }

    private static void PostKey(IntPtr hwnd, byte vk, bool down, bool system)
    {
        var msg = system
            ? (down ? WmSysKeyDown : WmSysKeyUp)
            : (down ? WmKeyDown : WmKeyUp);
        PostMessage(hwnd, msg, (IntPtr)vk, IntPtr.Zero);
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

    private static IntPtr WheelWParam(double delta)
    {
        var amount = (int)Math.Round(delta, MidpointRounding.AwayFromZero);
        return (IntPtr)(amount << 16);
    }

    private static void InvokeProtectedOnClick(Control control)
    {
        var method = typeof(Control).GetMethod(
            "OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(control, new object[] { EventArgs.Empty });
    }

    private readonly struct KeyStroke
    {
        public KeyStroke(byte virtualKey, IReadOnlyList<byte> modifierVks, char? ch, bool isAlt)
        {
            VirtualKey = virtualKey;
            ModifierVks = modifierVks;
            Char = ch;
            IsAlt = isAlt;
        }

        public byte VirtualKey { get; }
        public IReadOnlyList<byte> ModifierVks { get; }
        public char? Char { get; }
        public bool IsAlt { get; }

        public static KeyStroke Parse(string keys)
        {
            var chord = KeyChord.Parse(keys);
            if (chord.IsPlainText)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown key '{keys}'.");

            var modifiers = new List<byte>(4);
            if ((chord.Modifiers & KeyModifier.Control) != 0) modifiers.Add(0x11);
            if ((chord.Modifiers & KeyModifier.Alt) != 0) modifiers.Add(0x12);
            if ((chord.Modifiers & KeyModifier.Shift) != 0) modifiers.Add(0x10);
            if ((chord.Modifiers & KeyModifier.Meta) != 0) modifiers.Add(0x5B);
            return FromCanonical(chord.KeyToken, modifiers, (chord.Modifiers & KeyModifier.Alt) != 0);
        }

        private static KeyStroke FromCanonical(string canonical, IReadOnlyList<byte> modifiers, bool isAlt)
        {
            if (canonical.Length == 1)
                return FromChar(canonical[0], modifiers, isAlt);
            if (canonical.Length == 2 && canonical[0] == 'D' && char.IsDigit(canonical[1]))
                return FromChar(canonical[1], modifiers, isAlt);

            var special = SpecialVk(canonical)
                ?? throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown key '{canonical}'.");
            return new KeyStroke(special, modifiers, ch: null, isAlt);
        }

        private static KeyStroke FromChar(char ch, IReadOnlyList<byte> modifiers, bool isAlt)
        {
            var vk = (byte)VkKeyScan(ch);
            if (vk == 0xFF)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown key '{ch}'.");
            return new KeyStroke(vk, modifiers, ch, isAlt);
        }

        private static byte? SpecialVk(string key) => key switch
        {
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "SPACE" => 0x20,
            var value when value.Length is 2 or 3 && value[0] == 'F' && int.TryParse(value[1..], out var fn) && fn is >= 1 and <= 12
                => (byte)(0x70 + fn - 1),
            _ => null,
        };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char ch);
}
