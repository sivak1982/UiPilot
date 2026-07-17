using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfPilot.Interaction;

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
}
