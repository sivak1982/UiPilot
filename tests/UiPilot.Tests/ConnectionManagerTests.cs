using System.Reflection;
using UiPilot.Cli;
using UiPilot.Cli.Process;
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
                     "start_app", "start_process", "wait_for_log", "restart_app", "detach", "stop_app", "stop_all",
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

public class StartupHookLocatorTests
{
    [Fact]
    public void DetectUiFramework_FindsAvalonia()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Avalonia.dll"), Array.Empty<byte>());
            Assert.Equal(UiFrameworks.Avalonia, StartupHookLocator.DetectUiFramework(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectUiFramework_FindsWpf()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "PresentationFramework.dll"), Array.Empty<byte>());
            Assert.Equal(UiFrameworks.Wpf, StartupHookLocator.DetectUiFramework(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyTo_SetsDotnetStartupHooks()
    {
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "app");
        var hookDir = Path.Combine(root, "cli", "hooks", "avalonia");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(hookDir);
        try
        {
            File.WriteAllBytes(Path.Combine(appDir, "Avalonia.dll"), Array.Empty<byte>());
            var hookDll = Path.Combine(hookDir, "UiPilot.Avalonia.StartupHook.dll");
            File.WriteAllBytes(hookDll, Array.Empty<byte>());

            var psi = new System.Diagnostics.ProcessStartInfo();
            var applied = StartupHookLocator.ApplyTo(
                psi,
                appDir,
                uiFramework: null,
                useStartupHook: true,
                baseDirectory: Path.Combine(root, "cli"));

            Assert.Equal(hookDll, applied);
            Assert.Equal(hookDll, psi.Environment[StartupHookLocator.EnvVarName]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyTo_RespectsDisableEnv()
    {
        var previous = Environment.GetEnvironmentVariable(StartupHookLocator.DisableEnvVarName);
        try
        {
            Environment.SetEnvironmentVariable(StartupHookLocator.DisableEnvVarName, "0");
            var psi = new System.Diagnostics.ProcessStartInfo();
            var applied = StartupHookLocator.ApplyTo(psi, Path.GetTempPath(), "avalonia", useStartupHook: true);
            Assert.Null(applied);
        }
        finally
        {
            Environment.SetEnvironmentVariable(StartupHookLocator.DisableEnvVarName, previous);
        }
    }

    [Fact]
    public void ApplyTo_UseStartupHookFalse_Skips()
    {
        var appDir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDir);
        try
        {
            File.WriteAllBytes(Path.Combine(appDir, "Avalonia.Controls.dll"), Array.Empty<byte>());
            var psi = new System.Diagnostics.ProcessStartInfo();
            Assert.Null(StartupHookLocator.ApplyTo(psi, appDir, "avalonia", useStartupHook: false));
            Assert.False(psi.Environment.ContainsKey(StartupHookLocator.EnvVarName));
        }
        finally
        {
            Directory.Delete(appDir, recursive: true);
        }
    }
}

