using System.Reflection;
using UiPilot.Cli;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class ConnectionManagerTests
{
    [Fact]
    public async Task SendWithoutAttachment_DoesNotAutoAttach()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.SendAsync(ToolCatalog.ListWindows, new { }));

        Assert.Equal(PilotErrorCodes.NotAttached, ex.Code);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public async Task AttachMissingPid_ReturnsNotFoundCode()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.AttachAsync(-1));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public async Task RestartWithoutSession_ReturnsNotAttachedCode()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.RestartAsync());

        Assert.Equal(PilotErrorCodes.NotAttached, ex.Code);
    }

    [Fact]
    public void ListSessions_InitiallyEmpty()
    {
        using var manager = new ConnectionManager();

        Assert.Empty(manager.ListSessions());
        Assert.Null(manager.ActiveSessionName);
    }

    [Fact]
    public void SelectSession_Missing_ReturnsNotFound()
    {
        using var manager = new ConnectionManager();

        var ex = Assert.Throws<PilotCliException>(() => manager.SelectSession("oi"));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
        Assert.Contains("oi", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectSession_EmptyName_ReturnsInvalidArgs()
    {
        using var manager = new ConnectionManager();

        var ex = Assert.Throws<PilotCliException>(() => manager.SelectSession("  "));

        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public async Task StartApp_MissingFile_ReturnsNotFound()
    {
        using var manager = new ConnectionManager();
        var missing = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"), "MissingApp.exe");

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.StartAppAsync(missing, session: "sim"));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public async Task StartApp_EmptyPath_ReturnsInvalidArgs()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.StartAppAsync(" "));

        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public void StopAll_WhenEmpty_ReturnsEmpty()
    {
        using var manager = new ConnectionManager();

        Assert.Empty(manager.StopAll());
        Assert.Empty(manager.ListSessions());
    }

    [Fact]
    public void StopApp_WhenEmpty_ReturnsNull()
    {
        using var manager = new ConnectionManager();

        Assert.Null(manager.StopApp());
    }

    [Fact]
    public void Detach_WhenEmpty_ReturnsNull()
    {
        using var manager = new ConnectionManager();

        Assert.Null(manager.Detach());
    }

    [Fact]
    public async Task Send_UnknownSession_ReturnsNotFound()
    {
        using var manager = new ConnectionManager();

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.SendAsync(ToolCatalog.ListWindows, new { }, session: "sim"));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
    }
}

public class LifecycleToolSurfaceTests
{
    [Fact]
    public void LifecycleTools_ExposeMultiSessionTools()
    {
        var names = typeof(UiPilot.Cli.Tools.LifecycleTools)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(GetMcpToolName)
            .Where(n => n != null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[]
                 {
                     "list_apps", "list_sessions", "select_session", "attach", "build_and_start",
                     "start_app", "restart_app", "detach", "stop_app", "stop_all",
                 })
        {
            Assert.Contains(required, names);
        }
    }

    [Fact]
    public void ForwardingTools_SessionParameter_IsPresentOnBuiltIns()
    {
        var methods = typeof(UiPilot.Cli.Tools.ForwardingTools)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            if (GetMcpToolName(method) is null) continue;
            var hasSession = method.GetParameters().Any(p =>
                string.Equals(p.Name, "session", StringComparison.Ordinal));
            Assert.True(hasSession, $"{method.Name} should expose an optional session parameter.");
        }
    }

    private static string? GetMcpToolName(System.Reflection.MethodInfo method)
    {
        var attribute = method.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().FullName == "ModelContextProtocol.Server.McpServerToolAttribute");
        return attribute?.GetType().GetProperty("Name")?.GetValue(attribute) as string;
    }
}

public class AppLauncherTests
{
    [Fact]
    public void Start_MissingPath_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"), "Nope.exe");

        Assert.Throws<FileNotFoundException>(() =>
            UiPilot.Cli.Process.AppLauncher.Start(missing));
    }
}
