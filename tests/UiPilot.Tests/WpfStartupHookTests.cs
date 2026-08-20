using UiPilot.Client;
using Xunit;

namespace UiPilot.Tests;

public sealed class WpfStartupHookTests
{
    [Fact]
    public async Task WpfSample_StartsThroughGenericHook()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var app = Path.Combine(
            FindRepoRoot(),
            "samples",
            "SampleApp",
            "bin",
            "Debug",
            "net8.0-windows",
            "SampleApp.exe");
        Assert.True(File.Exists(app), $"Sample app was not built: {app}");

        await using var pilot = new UiPilotClient();
        var session = await pilot.StartAppAsync(app, session: "wpf-generic-hook");
        Assert.Equal("wpf", session.UiFramework);

        var nameBox = (await pilot.WaitForElementAsync(
            "NameBox", exact: true, session: "wpf-generic-hook")).Single();
        Assert.Equal("TextBox", nameBox.Type);
        var typed = await pilot.TypeTextAsync(
            nameBox.Id, "Generic Hook", session: "wpf-generic-hook");
        Assert.Equal("synthetic:automation-setvalue", typed.Method);

        var greet = (await pilot.WaitForElementAsync(
            "GreetButton", exact: true, session: "wpf-generic-hook")).Single();
        await pilot.FocusAsync(greet.Id, session: "wpf-generic-hook");

        var clicked = await pilot.ClickAsync(greet.Id, session: "wpf-generic-hook");
        Assert.Equal("synthetic:automation-invoke", clicked.Method);
        var greeting = await pilot.WaitForElementAsync(
            "Hello, Generic Hook!", exact: true, session: "wpf-generic-hook");
        Assert.Contains(greeting.Elements, element => element.Visible);
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
