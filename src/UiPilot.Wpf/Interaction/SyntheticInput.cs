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
using System.Windows.Media;
using UiPilot.Tools;
using UiPilot.Inspection;
using UiPilot.Media;
using UiPilot.Interaction;

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
        var value = text ?? string.Empty;

        // Prefer SetPassword(string) when present (secure password boxes that mask Text).
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

        if (obj is System.Windows.Controls.PasswordBox passwordBox)
        {
            passwordBox.Password = value;
            return "synthetic:passwordbox-set";
        }

        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Value) is IValueProvider valueProvider && !valueProvider.IsReadOnly)
        {
            valueProvider.SetValue(value);
            return "synthetic:automation-setvalue";
        }

        if (obj is TextBoxBase textBox && obj is System.Windows.Controls.TextBox tb)
        {
            tb.Text = value;
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

        var chord = KeyChord.Parse(keys);
        if (chord.IsPlainText)
        {
            RaiseText(element, chord.KeyToken);
            return "synthetic:keys";
        }

        RaiseKeyStroke(element, MapKey(chord.KeyToken), MapModifiers(chord.Modifiers));
        return "synthetic:keys";
    }

    public static string Scroll(DependencyObject obj, double dx, double dy)
    {
        if (obj is not UIElement element)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Target is not a UIElement.");

        var (vertical, horizontal) = ScrollMetrics.Axes(dx, dy);
        if (vertical == 0 && horizontal == 0)
            return "synthetic:scroll";

        if (vertical != 0)
        {
            RaiseMouseWheel(element, UIElement.PreviewMouseWheelEvent, vertical);
            RaiseMouseWheel(element, UIElement.MouseWheelEvent, vertical);
        }

        if (horizontal != 0)
            ScrollHorizontally(element, horizontal);

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

        // Synthetic KeyEventArgs do not update Keyboard.Modifiers, so InputBindings that key off
        // Ctrl/Alt/Shift would otherwise silently no-op. Execute matching bindings explicitly.
        if (modifiers != ModifierKeys.None)
            TryExecuteInputBindings(element, key, modifiers);

        RaiseKey(element, key, UIElement.PreviewKeyUpEvent);
        RaiseKey(element, key, Keyboard.KeyUpEvent);

        for (var i = modifierKeys.Count - 1; i >= 0; i--)
            RaiseKey(element, modifierKeys[i], UIElement.PreviewKeyUpEvent);
        for (var i = modifierKeys.Count - 1; i >= 0; i--)
            RaiseKey(element, modifierKeys[i], Keyboard.KeyUpEvent);
    }

    private static void TryExecuteInputBindings(DependencyObject start, Key key, ModifierKeys modifiers)
    {
        for (var current = start; current != null; )
        {
            if (current is UIElement uie)
            {
                foreach (InputBinding binding in uie.InputBindings)
                {
                    if (binding is KeyBinding kb &&
                        kb.Key == key &&
                        kb.Modifiers == modifiers &&
                        kb.Command != null &&
                        kb.Command.CanExecute(kb.CommandParameter))
                    {
                        kb.Command.Execute(kb.CommandParameter);
                        return;
                    }
                }
            }

            var parent = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            if (parent == null && current is FrameworkElement fe)
                parent = fe.Parent;
            current = parent;
        }

        if (Application.Current?.MainWindow is UIElement window && !ReferenceEquals(window, start))
        {
            foreach (InputBinding binding in window.InputBindings)
            {
                if (binding is KeyBinding kb &&
                    kb.Key == key &&
                    kb.Modifiers == modifiers &&
                    kb.Command != null &&
                    kb.Command.CanExecute(kb.CommandParameter))
                {
                    kb.Command.Execute(kb.CommandParameter);
                    return;
                }
            }
        }
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

    private static void ScrollHorizontally(DependencyObject start, int wheelDelta)
    {
        var current = start;
        while (current != null)
        {
            if (current is ScrollViewer viewer)
            {
                var lines = wheelDelta / (double)ScrollMetrics.WheelDeltaPerLine;
                viewer.ScrollToHorizontalOffset(Math.Max(0, viewer.HorizontalOffset + (lines * 16)));
                return;
            }

            current = current is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(current);
        }
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

    private static ModifierKeys MapModifiers(KeyModifier modifiers)
    {
        var mapped = ModifierKeys.None;
        if ((modifiers & KeyModifier.Control) != 0) mapped |= ModifierKeys.Control;
        if ((modifiers & KeyModifier.Alt) != 0) mapped |= ModifierKeys.Alt;
        if ((modifiers & KeyModifier.Shift) != 0) mapped |= ModifierKeys.Shift;
        if ((modifiers & KeyModifier.Meta) != 0) mapped |= ModifierKeys.Windows;
        return mapped;
    }

    private static Key MapKey(string canonical)
    {
        switch (canonical)
        {
            case "ENTER": return Key.Return;
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
