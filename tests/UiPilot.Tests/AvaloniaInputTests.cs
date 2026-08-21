using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Xunit;

namespace UiPilot.Tests;

public sealed class AvaloniaInputTests
{
    [Fact]
    public void Click_Button_UsesAutomationInvokeWhenPeerSupportsIt()
    {
        var button = new Button { Content = "Go" };
        var method = global::UiPilot.Avalonia.Input.Click(button);
        Assert.Equal("synthetic:automation-invoke", method);
    }

    [Fact]
    public void Click_Toggle_StillHasTypedFallback()
    {
        var toggle = new ToggleButton();
        var method = global::UiPilot.Avalonia.Input.Click(toggle);
        Assert.Equal("synthetic:toggle", method);
    }
}
