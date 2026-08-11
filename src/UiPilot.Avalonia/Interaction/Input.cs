using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

internal static class Input
{
    public static string Click(Visual obj)
    {
        if (obj is not Control control)
            throw new InvalidOperationException("Target is not a Control.");

        if (obj is MenuItem menuItem)
        {
            if (menuItem.ItemCount > 0)
            {
                menuItem.IsSubMenuOpen = true;
                return "synthetic:menuitem-expand";
            }

            if (menuItem.Command != null && menuItem.Command.CanExecute(menuItem.CommandParameter))
                menuItem.Command.Execute(menuItem.CommandParameter);
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return "synthetic:menuitem-click";
        }

        if (obj is TabItem tabItem)
        {
            tabItem.IsSelected = true;
            return "synthetic:tabitem-select";
        }

        if (obj is RadioButton radio)
        {
            radio.IsChecked = true;
            radio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return "synthetic:radio-select";
        }

        if (obj is ToggleButton toggle)
        {
            toggle.IsChecked = !(toggle.IsChecked ?? false);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return "synthetic:toggle";
        }

        if (obj is Button button)
        {
            if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                return "synthetic:button-command";
            }

            control.Focus();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return "synthetic:raise-click";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support synthetic click.");
    }

    public static string TypeText(Visual obj, string text)
    {
        if (obj is not Control control)
            throw new InvalidOperationException("Target is not a Control.");

        control.Focus();
        var value = text ?? string.Empty;

        // Prefer SetPassword(string) when present (e.g. secure password boxes that keep
        // Text as a mask). Reflection keeps this generic for any control that exposes it.
        var setPassword = obj.GetType().GetMethod(
            "SetPassword",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);
        if (setPassword != null)
        {
            setPassword.Invoke(obj, new object[] { value });
            return "synthetic:setpassword";
        }

        if (obj is TextBox textBox)
        {
            textBox.Text = value;
            return "synthetic:textbox-set";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' does not support text entry.");
    }

    public static string PressKeys(Visual? obj, string keys)
    {
        if (string.IsNullOrEmpty(keys))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Keys cannot be empty.");

        var input = ResolveInputElement(obj);
        if (obj != null)
            input.Focus();

        if (keys.IndexOf("+", StringComparison.Ordinal) < 0)
        {
            if (TryParseKey(keys, out var specialKey))
            {
                RaiseKeyStroke(input, specialKey, KeyModifiers.None);
                return "synthetic:keys";
            }

            RaiseText(input, keys);
            return "synthetic:keys";
        }

        if (!StartsWithModifier(keys))
        {
            RaiseText(input, keys);
            return "synthetic:keys";
        }

        var stroke = KeyStroke.Parse(keys);
        RaiseKeyStroke(input, stroke.Key, stroke.Modifiers);
        return "synthetic:keys";
    }

    public static string Scroll(Visual obj, double dx, double dy)
    {
        if (obj is not Control control)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a Control.");

        var delta = new Vector(dx, dy);
        if (delta == default)
            return "synthetic:scroll";

        var args = new PointerWheelEventArgs(
            control,
            null!,
            control,
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            unchecked((ulong)Environment.TickCount64),
            default!,
            KeyModifiers.None,
            delta)
        {
            RoutedEvent = InputElement.PointerWheelChangedEvent,
        };
        control.RaiseEvent(args);
        return "synthetic:scroll";
    }

    public static string Focus(Visual obj)
    {
        if (obj is not InputElement input)
            throw new InvalidOperationException("Target is not an InputElement.");
        input.Focus();
        return "synthetic:focus";
    }

    public static string SelectItem(Visual obj, string? text, int? index)
    {
        if (index == null && string.IsNullOrWhiteSpace(text))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Provide either 'index' or 'text'.");

        if (obj is SelectingItemsControl selecting)
            return SelectFromSelectingItemsControl(selecting, text, index);

        if (obj is MenuItem menuItem && menuItem.ItemCount > 0)
            return SelectFromMenu(menuItem, text, index);

        throw new PilotToolException(
            PilotErrorCodes.Unsupported,
            $"Element of type '{obj.GetType().Name}' does not support item selection.");
    }

    public static string InvokeCommand(Visual obj)
    {
        if (obj is Button button && button.Command != null)
        {
            if (button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                return "command-executed";
            }
            return "command-cannot-execute";
        }

        if (obj is MenuItem menuItem && menuItem.Command != null)
        {
            if (menuItem.Command.CanExecute(menuItem.CommandParameter))
            {
                menuItem.Command.Execute(menuItem.CommandParameter);
                return "command-executed";
            }
            return "command-cannot-execute";
        }

        throw new InvalidOperationException(
            $"Element of type '{obj.GetType().Name}' has no bound ICommand.");
    }

    private static InputElement ResolveInputElement(Visual? obj)
    {
        if (obj is InputElement input)
            return input;
        if (obj != null)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not an InputElement.");
        if (WindowOps.Resolve(null)?.FocusManager?.GetFocusedElement() is InputElement focused)
            return focused;
        if (WindowOps.Resolve(null) is InputElement window)
            return window;
        throw new PilotToolException(PilotErrorCodes.InvalidArgs, "No target element or focused Avalonia element is available.");
    }

    private static void RaiseText(InputElement input, string text)
    {
        input.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Source = input,
            Text = text,
        });
    }

    private static void RaiseKeyStroke(InputElement input, Key key, KeyModifiers modifiers)
    {
        var modifierKeys = ModifierKeysFor(modifiers);
        foreach (var modifierKey in modifierKeys)
            RaiseKey(input, modifierKey, KeyModifiers.None, InputElement.KeyDownEvent);

        RaiseKey(input, key, modifiers, InputElement.KeyDownEvent);
        RaiseKey(input, key, modifiers, InputElement.KeyUpEvent);

        for (var i = modifierKeys.Count - 1; i >= 0; i--)
            RaiseKey(input, modifierKeys[i], KeyModifiers.None, InputElement.KeyUpEvent);
    }

    private static void RaiseKey(InputElement input, Key key, KeyModifiers modifiers, RoutedEvent routedEvent)
    {
        input.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = routedEvent,
            Source = input,
            Key = key,
            KeyModifiers = modifiers,
        });
    }

    private static string SelectFromSelectingItemsControl(SelectingItemsControl selecting, string? text, int? index)
    {
        if (index.HasValue)
        {
            var value = index.Value;
            if (value < 0 || value >= selecting.ItemCount)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Selection index {value} is out of range.");
            selecting.SelectedIndex = value;
            return "synthetic:select-index";
        }

        var match = FindItem(selecting.Items, text!);
        if (!match.Found)
            throw new PilotToolException(PilotErrorCodes.NotFound, $"No selectable item matching '{text}' was found.");
        selecting.SelectedItem = match.Item;
        return "synthetic:select-text";
    }

    private static string SelectFromMenu(MenuItem menuItem, string? text, int? index)
    {
        menuItem.IsSubMenuOpen = true;
        var children = new List<MenuItem>();
        foreach (var item in menuItem.Items)
            if (item is MenuItem child)
                children.Add(child);

        MenuItem match;
        if (index.HasValue)
        {
            var value = index.Value;
            if (value < 0 || value >= children.Count)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Selection index {value} is out of range.");
            match = children[value];
        }
        else
        {
            match = null!;
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

    private static IReadOnlyList<Key> ModifierKeysFor(KeyModifiers modifiers)
    {
        var keys = new List<Key>(4);
        if ((modifiers & KeyModifiers.Control) != 0) keys.Add(Key.LeftCtrl);
        if ((modifiers & KeyModifiers.Alt) != 0) keys.Add(Key.LeftAlt);
        if ((modifiers & KeyModifiers.Shift) != 0) keys.Add(Key.LeftShift);
        if ((modifiers & KeyModifiers.Meta) != 0) keys.Add(Key.LWin);
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
            case "ENTER": key = Key.Enter; return true;
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
        return IsModifierToken(first);
    }

    private static bool IsModifierToken(string token)
    {
        switch (NormalizeKeyToken(token))
        {
            case "CTRL":
            case "CONTROL":
            case "ALT":
            case "SHIFT":
            case "WIN":
            case "WINDOWS":
            case "CMD":
            case "COMMAND":
            case "META":
                return true;
            default:
                return false;
        }
    }

    private readonly struct KeyStroke
    {
        private KeyStroke(Key key, KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        public Key Key { get; }

        public KeyModifiers Modifiers { get; }

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

            var modifiers = KeyModifiers.None;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                switch (NormalizeKeyToken(parts[i]))
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= KeyModifiers.Control;
                        break;
                    case "ALT":
                        modifiers |= KeyModifiers.Alt;
                        break;
                    case "SHIFT":
                        modifiers |= KeyModifiers.Shift;
                        break;
                    case "WIN":
                    case "WINDOWS":
                    case "CMD":
                    case "COMMAND":
                    case "META":
                        modifiers |= KeyModifiers.Meta;
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
