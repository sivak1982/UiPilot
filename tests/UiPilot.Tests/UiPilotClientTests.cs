using UiPilot.Client;
using Xunit;

namespace UiPilot.Tests;

public sealed class UiPilotClientTests
{
    [Fact]
    public async Task AgentAuthoredFlow_CanBeFrozenAsTypedCSharp()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = FindRepoRoot();
        var app = Path.Combine(
            root,
            "samples",
            "AvaloniaSampleApp",
            "bin",
            "Debug",
            "net8.0",
            "AvaloniaSampleApp.exe");
        Assert.True(File.Exists(app), $"Sample app was not built: {app}");

        await using var pilot = new UiPilotClient();
        await pilot.StartAppAsync(app, session: "sample");

        var resized = await pilot.ResizeWindowAsync(
            width: 900,
            height: 650,
            session: "sample");
        Assert.Equal("normal", resized.State);
        Assert.Equal(900, resized.Width);
        Assert.Equal(650, resized.Height);

        var nameBox = (await pilot.WaitForElementAsync(
            "NameBox", exact: true, session: "sample")).Single();
        var typed = await pilot.TypeTextAsync(nameBox.Id, "UiPilot", session: "sample");
        Assert.Equal("synthetic:textbox-set", typed.Method);

        var greet = (await pilot.WaitForElementAsync(
            "GreetButton", exact: true, session: "sample")).Single();
        await pilot.FocusAsync(greet.Id, session: "sample"); // commits the TextBox binding

        // The control can enter the visual tree before its command binding is ready. Because every
        // call returns its interaction method, ordinary C# can retry the exact condition this test
        // cares about instead of adding a click-until-* verb to UiPilot.
        InteractionResult? clicked = null;
        var clickDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < clickDeadline)
        {
            clicked = await pilot.ClickAsync(greet.Id, session: "sample");
            if (clicked.Method == "synthetic:button-command")
                break;
            await Task.Delay(200);
        }
        Assert.NotNull(clicked);
        Assert.Equal("synthetic:button-command", clicked.Method);

        var greeting = await pilot.WaitForElementAsync(
            "Hello, UiPilot!", exact: true, session: "sample");
        Assert.Contains(greeting.Elements, element =>
            element.Visible && element.Text == "Hello, UiPilot!");
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "UiPilot.sln")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the UiPilot repository root.");
    }
}
