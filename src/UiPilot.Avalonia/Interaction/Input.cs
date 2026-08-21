using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;
using UiPilot.Interaction;

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

        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        if (peer is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return "synthetic:automation-invoke";
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
            control.Focus();
            if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
            {
                button.Command.Execute(button.CommandParameter);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return "synthetic:button-command";
            }

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

        var chord = KeyChord.Parse(keys);
        if (chord.IsPlainText)
        {
            RaiseText(input, chord.KeyToken);
            return "synthetic:keys";
        }

        RaiseKeyStroke(input, MapKey(chord.KeyToken), MapModifiers(chord.Modifiers));
        return "synthetic:keys";
    }

    public static string Scroll(Visual obj, double dx, double dy)
    {
        if (obj is not Control control)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a Control.");

        // Contract: dx/dy are scroll lines (Avalonia PointerWheelEventArgs.Delta is in lines).
        var delta = new Vector(dx, dy);
        if (delta == default)
            return "synthetic:scroll";

        var props = new PointerPointProperties();
        var args = new PointerWheelEventArgs(
            control,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            control,
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
            unchecked((ulong)Environment.TickCount64),
            props,
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

    private static KeyModifiers MapModifiers(KeyModifier modifiers)
    {
        var mapped = KeyModifiers.None;
        if ((modifiers & KeyModifier.Control) != 0) mapped |= KeyModifiers.Control;
        if ((modifiers & KeyModifier.Alt) != 0) mapped |= KeyModifiers.Alt;
        if ((modifiers & KeyModifier.Shift) != 0) mapped |= KeyModifiers.Shift;
        if ((modifiers & KeyModifier.Meta) != 0) mapped |= KeyModifiers.Meta;
        return mapped;
    }

    private static Key MapKey(string canonical)
    {
        switch (canonical)
        {
            case "ENTER": return Key.Enter;
            case "TAB": return Key.Tab;
            case "ESCAPE": return Key.Escape;
            case "BACKSPACE": return Key.Back;
            case "DELETE": return Key.Delete;
            case "LEFT": return Key.Left;
            case "RIGHT": return Key.Right;
            case "UP": return Key.Up;
            case "DOWN": return Key.Down;
            case "HOME": return Key.Home;
            case "END": return Key.End;
            case "PAGEUP": return Key.PageUp;
            case "PAGEDOWN": return Key.PageDown;
            case "SPACE": return Key.Space;
        }

        if (canonical.Length >= 2 && canonical[0] == 'F' &&
            int.TryParse(canonical.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn))
            return (Key)((int)Key.F1 + fn - 1);

        return (Key)Enum.Parse(typeof(Key), canonical);
    }
}
