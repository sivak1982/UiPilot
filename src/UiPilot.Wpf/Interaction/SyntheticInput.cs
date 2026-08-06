using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf.Interaction;

/// <summary>
/// Synthetic interaction: drives elements via UI Automation peers with a RaiseEvent fallback.
/// This is deliberately labeled "synthetic" - it does not go through real hit-testing, mouse
/// capture, or Preview* input the way SendInput would. Real-input mode is a post-MVP option.
/// All methods must be called on the WPF UI thread.
/// </summary>
public static class SyntheticInput
{
    public static string Click(DependencyObject obj)
    {
        if (obj is not UIElement element)
            throw new InvalidOperationException("Target is not a UIElement.");

        // Menus are special: submenu items live in popups and top-level items only expose
        // ExpandCollapse, so the generic Invoke path can't drive menu navigation. Handle them
        // directly (works even for items whose popup has never been opened / realized).
        if (obj is System.Windows.Controls.MenuItem menuItem)
        {
            if (menuItem.HasItems)
            {
                menuItem.IsSubmenuOpen = true;
                return "synthetic:menuitem-expand";
            }

            if (menuItem.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
                menuItem.Command.Execute(menuItem.CommandParameter);
            menuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            return "synthetic:menuitem-click";
        }

        if (obj is TabItem tabItem)
        {
            tabItem.IsSelected = true;
            return "synthetic:tabitem-select";
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return "synthetic:automation-invoke";
        }

        if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
        {
            toggle.Toggle();
            return "synthetic:automation-toggle";
        }

        if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand)
        {
            expand.Expand();
            return "synthetic:automation-expand";
        }

        if (obj is RadioButton radio)
        {
            radio.IsChecked = true;
            radio.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return "synthetic:radio-select";
        }

        if (obj is ToggleButton toggleButton)
        {
            toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
            toggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return "synthetic:toggle";
        }

        if (obj is ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return "synthetic:raise-click";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support click via automation or ButtonBase.");
    }

    public static string TypeText(DependencyObject obj, string text)
    {
        if (obj is not UIElement element)
            throw new InvalidOperationException("Target is not a UIElement.");

        element.Focus();
        Keyboard.Focus(element as IInputElement);

        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Value) is IValueProvider value && !value.IsReadOnly)
        {
            value.SetValue(text ?? string.Empty);
            return "synthetic:automation-setvalue";
        }

        if (obj is TextBoxBase textBox && obj is System.Windows.Controls.TextBox tb)
        {
            tb.Text = text ?? string.Empty;
            return "synthetic:textbox-set";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support text entry.");
    }

    public static string PressKeys(DependencyObject? obj, string keys)
    {
        if (string.IsNullOrEmpty(keys))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Keys cannot be empty.");

        var element = ResolveInputElement(obj);
        if (obj != null)
            FocusElement(element);

        if (keys.IndexOf("+", StringComparison.Ordinal) < 0)
        {
            if (TryParseKey(keys, out var specialKey))
            {
                RaiseKeyStroke(element, specialKey, ModifierKeys.None);
                return "synthetic:keys";
            }

            RaiseText(element, keys);
            return "synthetic:keys";
        }

        if (StartsWithModifier(keys))
        {
            var stroke = KeyStroke.Parse(keys);
            RaiseKeyStroke(element, stroke.Key, stroke.Modifiers);
            return "synthetic:keys";
        }

        RaiseText(element, keys);
        return "synthetic:keys";
    }

    public static string Scroll(DependencyObject obj, double dx, double dy)
    {
        if (obj is not UIElement element)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a UIElement.");

        var delta = WheelDelta(dy);
        if (delta == 0 && Math.Abs(dx) > 0)
            delta = WheelDelta(dx);
        if (delta == 0)
            return "synthetic:scroll";

        RaiseMouseWheel(element, UIElement.PreviewMouseWheelEvent, delta);
        RaiseMouseWheel(element, UIElement.MouseWheelEvent, delta);
        return "synthetic:scroll";
    }

    public static string SelectItem(DependencyObject obj, string? text, int? index)
    {
        if (index == null && string.IsNullOrWhiteSpace(text))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide either 'index' or 'text'.");

        if (obj is Selector selector)
            return SelectFromSelector(selector, text, index);

        if (obj is System.Windows.Controls.MenuItem menuItem && menuItem.HasItems)
            return SelectFromMenu(menuItem, text, index);

        throw new PilotToolException(
            PilotErrorCodes.Unsupported,
            $"Element of type '{obj.GetType().Name}' does not support item selection.");
    }

    public static string InvokeCommand(DependencyObject obj)
    {
        if (obj is ICommandSource source && source.Command != null)
        {
            if (source.Command.CanExecute(source.CommandParameter))
            {
                source.Command.Execute(source.CommandParameter);
                return "command-executed";
            }
            return "command-cannot-execute";
        }
        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' has no bound ICommand.");
    }

    private static UIElement ResolveInputElement(DependencyObject? obj)
    {
        if (obj is UIElement element)
            return element;
        if (obj != null)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a UIElement.");
        if (Keyboard.FocusedElement is UIElement focused)
            return focused;
        if (Application.Current?.MainWindow is UIElement window)
            return window;
        throw new PilotToolException(PilotErrorCodes.InvalidArgs, "No target element or focused WPF element is available.");
    }

    private static void FocusElement(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element as IInputElement);
    }

    private static void RaiseText(UIElement element, string text)
    {
        var composition = new TextComposition(InputManager.Current, element, text);
        var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
        {
            RoutedEvent = TextCompositionManager.TextInputEvent,
            Source = element,
        };
        element.RaiseEvent(args);
    }

    private static void RaiseKeyStroke(UIElement element, Key key, ModifierKeys modifiers)
    {
        var modifierKeys = ModifierKeysFor(modifiers);
        foreach (var modifierKey in modifierKeys)
            RaiseKey(element, modifierKey, UIElement.PreviewKeyDownEvent);
        foreach (var modifierKey in modifierKeys)
            RaiseKey(element, modifierKey, Keyboard.KeyDownEvent);

        RaiseKey(element, key, UIElement.PreviewKeyDownEvent);
        RaiseKey(element, key, Keyboard.KeyDownEvent);
        RaiseKey(element, key, UIElement.PreviewKeyUpEvent);
        RaiseKey(element, key, Keyboard.KeyUpEvent);

        for (var i = modifierKeys.Count - 1; i >= 0; i--)
            RaiseKey(element, modifierKeys[i], UIElement.PreviewKeyUpEvent);
        for (var i = modifierKeys.Count - 1; i >= 0; i--)
            RaiseKey(element, modifierKeys[i], Keyboard.KeyUpEvent);
    }

    private static void RaiseKey(UIElement element, Key key, RoutedEvent routedEvent)
    {
        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            PresentationSource.FromDependencyObject(element),
            Environment.TickCount,
            key)
        {
            RoutedEvent = routedEvent,
            Source = element,
        };
        element.RaiseEvent(args);
    }

    private static void RaiseMouseWheel(UIElement element, RoutedEvent routedEvent, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = routedEvent,
            Source = element,
        };
        element.RaiseEvent(args);
    }

    private static int WheelDelta(double value)
    {
        if (value == 0) return 0;
        if (Math.Abs(value) < 1) return Math.Sign(value) * 120;
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string SelectFromSelector(Selector selector, string? text, int? index)
    {
        if (index.HasValue)
        {
            var value = index.Value;
            if (value < 0 || value >= selector.Items.Count)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Selection index {value} is out of range.");
            selector.SelectedIndex = value;
            return "synthetic:select-index";
        }

        var match = FindItem(selector.Items, text!);
        if (!match.Found)
            throw new PilotToolException(PilotErrorCodes.NotFound, $"No selectable item matching '{text}' was found.");
        selector.SelectedItem = match.Item;
        return "synthetic:select-text";
    }

    private static string SelectFromMenu(System.Windows.Controls.MenuItem menuItem, string? text, int? index)
    {
        menuItem.IsSubmenuOpen = true;
        var children = new List<System.Windows.Controls.MenuItem>();
        foreach (var item in menuItem.Items)
            if (item is System.Windows.Controls.MenuItem child)
                children.Add(child);

        System.Windows.Controls.MenuItem? match;
        if (index.HasValue)
        {
            var value = index.Value;
            if (value < 0 || value >= children.Count)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Selection index {value} is out of range.");
            match = children[value];
        }
        else
        {
            match = null;
            foreach (var child in children)
            {
                if (TextMatches(ItemText(child.Header ?? child), text!))
                {
                    match = child;
                    break;
                }
            }
            if (match == null)
                throw new PilotToolException(PilotErrorCodes.NotFound, $"No menu item matching '{text}' was found.");
        }

        Click(match);
        return index.HasValue ? "synthetic:select-index" : "synthetic:select-text";
    }

    private static (bool Found, object? Item) FindItem(IEnumerable items, string text)
    {
        foreach (var item in items)
        {
            if (TextMatches(ItemText(item), text))
                return (true, item);
        }
        return (false, null);
    }

    private static string? ItemText(object? item)
    {
        switch (item)
        {
            case null:
                return null;
            case ComboBoxItem combo:
                return ItemText(combo.Content) ?? combo.ToString();
            case ListBoxItem listBox:
                return ItemText(listBox.Content) ?? listBox.ToString();
            case TabItem tab:
                return ItemText(tab.Header) ?? ItemText(tab.Content) ?? tab.ToString();
            case HeaderedContentControl headered:
                return ItemText(headered.Header) ?? ItemText(headered.Content) ?? headered.ToString();
            case ContentControl content:
                return ItemText(content.Content) ?? content.ToString();
            case string s:
                return s;
            default:
                return item.ToString();
        }
    }

    private static bool TextMatches(string? candidate, string expected) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        string.Equals(candidate.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Key> ModifierKeysFor(ModifierKeys modifiers)
    {
        var keys = new List<Key>(4);
        if ((modifiers & ModifierKeys.Control) != 0) keys.Add(Key.LeftCtrl);
        if ((modifiers & ModifierKeys.Alt) != 0) keys.Add(Key.LeftAlt);
        if ((modifiers & ModifierKeys.Shift) != 0) keys.Add(Key.LeftShift);
        if ((modifiers & ModifierKeys.Windows) != 0) keys.Add(Key.LWin);
        return keys;
    }

    private static bool TryParseKey(string token, out Key key)
    {
        token = NormalizeKeyToken(token);
        key = Key.None;
        if (token.Length == 1)
        {
            var ch = token[0];
            if (ch >= 'A' && ch <= 'Z')
            {
                key = (Key)Enum.Parse(typeof(Key), ch.ToString(CultureInfo.InvariantCulture));
                return true;
            }
            if (ch >= '0' && ch <= '9')
            {
                key = (Key)Enum.Parse(typeof(Key), "D" + ch.ToString(CultureInfo.InvariantCulture));
                return true;
            }
        }

        switch (token)
        {
            case "ENTER": key = Key.Return; return true;
            case "RETURN": key = Key.Return; return true;
            case "TAB": key = Key.Tab; return true;
            case "ESC":
            case "ESCAPE": key = Key.Escape; return true;
            case "BACKSPACE": key = Key.Back; return true;
            case "BKSP": key = Key.Back; return true;
            case "DELETE":
            case "DEL": key = Key.Delete; return true;
            case "LEFT": key = Key.Left; return true;
            case "RIGHT": key = Key.Right; return true;
            case "UP": key = Key.Up; return true;
            case "DOWN": key = Key.Down; return true;
            case "HOME": key = Key.Home; return true;
            case "END": key = Key.End; return true;
            case "PAGEUP":
            case "PGUP": key = Key.PageUp; return true;
            case "PAGEDOWN":
            case "PGDN": key = Key.PageDown; return true;
            case "SPACE": key = Key.Space; return true;
        }

        if (token.Length >= 2 && token[0] == 'F' &&
            int.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn) &&
            fn >= 1 && fn <= 12)
        {
            key = (Key)((int)Key.F1 + fn - 1);
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Key.None;
    }

    private static string NormalizeKeyToken(string token) =>
        token.Trim().Replace(" ", string.Empty).ToUpperInvariant();

    private static bool StartsWithModifier(string keys)
    {
        var first = keys.Split(new[] { '+' }, 2)[0];
        switch (NormalizeKeyToken(first))
        {
            case "CTRL":
            case "CONTROL":
            case "ALT":
            case "SHIFT":
            case "WIN":
            case "WINDOWS":
            case "META":
            case "CMD":
            case "COMMAND":
                return true;
            default:
                return false;
        }
    }

    private readonly struct KeyStroke
    {
        private KeyStroke(Key key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public Key Key { get; }

        public ModifierKeys Modifiers { get; }

        public static KeyStroke Parse(string keys)
        {
            var rawParts = keys.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>(rawParts.Length);
            foreach (var part in rawParts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    parts.Add(trimmed);
            }
            if (parts.Count < 2)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Invalid key combination '{keys}'.");

            var modifiers = ModifierKeys.None;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                switch (NormalizeKeyToken(parts[i]))
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= ModifierKeys.Control;
                        break;
                    case "ALT":
                        modifiers |= ModifierKeys.Alt;
                        break;
                    case "SHIFT":
                        modifiers |= ModifierKeys.Shift;
                        break;
                    case "WIN":
                    case "WINDOWS":
                    case "META":
                    case "CMD":
                    case "COMMAND":
                        modifiers |= ModifierKeys.Windows;
                        break;
                    default:
                        throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown modifier '{parts[i]}'.");
                }
            }

            if (!TryParseKey(parts[parts.Count - 1], out var key))
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown key '{parts[parts.Count - 1]}'.");

            return new KeyStroke(key, modifiers);
        }
    }
}
