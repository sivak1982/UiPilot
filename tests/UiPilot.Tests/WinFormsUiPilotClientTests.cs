using UiPilot.Client;
using Xunit;

namespace UiPilot.Tests;

public sealed class WinFormsUiPilotClientTests
{
    [Fact]
    public async Task WinFormsSample_SupportsCoreAutomationFlow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var app = TestPaths.SampleApp("WinFormsSampleApp", "net8.0-windows", "WinFormsSampleApp.exe");
        Assert.True(File.Exists(app), $"Sample app was not built: {app}");

        await using var pilot = new UiPilotClient();
        var session = await pilot.StartAppAsync(app, session: "winforms");
        Assert.Equal("winforms", session.UiFramework);

        var resized = await pilot.ResizeWindowAsync(
            width: 800,
            height: 560,
            session: "winforms");
        Assert.Equal("normal", resized.State);
        Assert.Equal(800, resized.Width);
        Assert.Equal(560, resized.Height);

        var nameBox = (await pilot.WaitForElementAsync(
            "NameBox", exact: true, session: "winforms")).Single();
        Assert.Equal("TextBox", nameBox.Type);
        Assert.Equal("NameBox", nameBox.Name);
        var typed = await pilot.TypeTextAsync(nameBox.Id, "UiPilot", session: "winforms");
        Assert.Equal("synthetic:textbox-set", typed.Method);

        var colorCombo = (await pilot.WaitForElementAsync(
            "ColorCombo", exact: true, session: "winforms")).Single();
        var selected = await pilot.SelectItemAsync(
            colorCombo.Id, text: "Green", session: "winforms");
        Assert.Equal("synthetic:select-text", selected.Method);

        var greetButton = (await pilot.WaitForElementAsync(
            "GreetButton", exact: true, session: "winforms")).Single();
        Assert.Equal("Button", greetButton.Type);
        var clicked = await pilot.ClickAsync(greetButton.Id, session: "winforms");
        Assert.Equal("synthetic:perform-click", clicked.Method);

        var greeting = await pilot.WaitForElementAsync(
            "Hello, UiPilot!", exact: true, session: "winforms");
        Assert.Contains(greeting.Elements, element =>
            element.Visible && element.Name == "GreetingText");

        var actionsMenu = (await pilot.WaitForElementAsync(
            "ActionsMenu", exact: true, session: "winforms")).Single();
        Assert.Equal("ToolStripMenuItem", actionsMenu.Type);
        var expanded = await pilot.ClickAsync(actionsMenu.Id, session: "winforms");
        Assert.Equal("synthetic:toolstrip-expand", expanded.Method);

        var markReady = (await pilot.WaitForElementAsync(
            "MarkReadyMenuItem", exact: true, session: "winforms")).Single();
        var menuClicked = await pilot.ClickAsync(markReady.Id, session: "winforms");
        Assert.Equal("synthetic:toolstrip-click", menuClicked.Method);
        var menuResult = await pilot.WaitForElementAsync(
            "Menu action completed", exact: true, session: "winforms");
        Assert.Contains(menuResult.Elements, element => element.Name == "GreetingText");

        var normalShot = await pilot.ScreenshotAsync(session: "winforms");
        Assert.True(normalShot.Width > 0);
        Assert.True(normalShot.Height > 0);
        Assert.True(normalShot.GetBytes().Length > 100);

        var minimized = await pilot.SetWindowStateAsync(
            "minimized", session: "winforms");
        Assert.Equal("minimized", minimized.State);
        var minimizedShot = await pilot.ScreenshotAsync(session: "winforms");
        Assert.True(minimizedShot.Width > 0);
        Assert.True(minimizedShot.Height > 0);
        Assert.True(minimizedShot.GetBytes().Length > 100);
    }
}
