using System.Reflection;
using UiPilot.Client;
using UiPilot.Client.Process;
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
    public async Task ListSessions_RemovesProcessClosedOutsideUiPilot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var manager = new ConnectionManager();
        var command = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var started = await manager.StartProcessAsync(
            command,
            session: "external-close",
            arguments: "/c ping -n 120 127.0.0.1",
            showWindow: false);

        using (var process = System.Diagnostics.Process.GetProcessById(started.Pid))
        {
            process.Kill(entireProcessTree: true);
            Assert.True(process.WaitForExit(10_000));
        }

        Assert.Empty(manager.ListSessions());
        Assert.Null(manager.ActiveSessionName);

        var restarted = await manager.RestartAsync("external-close");
        Assert.Equal("external-close", restarted.Name);
        Assert.Contains(manager.ListSessions(), session => session.Name == "external-close");
        manager.StopAll();
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
            UiPilot.Client.Process.AppLauncher.Start(missing));
    }

    /// <summary>
    /// A supervisor-style host spawns service processes and can exit before them. Once the direct
    /// child is gone the parent/child link is too, so <c>Kill(entireProcessTree)</c> cannot reach
    /// the survivors — the job object created at launch can.
    /// </summary>
    [Fact]
    public void KillTree_StopsDetachedGrandchildren()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var before = PingPids();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // `start /b` detaches the ping, then cmd itself exits immediately.
        var parent = UiPilot.Client.Process.AppLauncher.StartProcess(
            cmd, arguments: "/c start /b ping -n 120 127.0.0.1", showWindow: false);
        try
        {
            var grandchild = WaitFor(
                () => PingPids().Except(before).Cast<int?>().FirstOrDefault(),
                pid => pid is not null,
                TimeSpan.FromSeconds(20));
            Assert.NotNull(grandchild);

            UiPilot.Client.Process.AppLauncher.KillTree(parent);

            var stillAlive = WaitFor(
                () => PingPids().Contains(grandchild!.Value),
                alive => !alive,
                TimeSpan.FromSeconds(20));
            Assert.False(stillAlive, "the detached grandchild was still running after KillTree.");
        }
        finally
        {
            UiPilot.Client.Process.AppLauncher.KillTree(parent);
            parent.Dispose();
        }
    }

    [Fact]
    public void KillByPid_UnknownPid_DoesNotThrow()
    {
        UiPilot.Client.Process.AppLauncher.KillByPid(-1);
    }

    private static HashSet<int> PingPids() =>
        System.Diagnostics.Process.GetProcessesByName("PING").Select(p => p.Id).ToHashSet();

    private static T WaitFor<T>(Func<T> read, Func<T, bool> done, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var value = read();
            if (done(value) || watch.Elapsed >= timeout)
                return value;
            Thread.Sleep(100);
        }
    }
}

public class StartupHookLocatorTests
{
    [Fact]
    public void ApplyTo_SetsGenericStartupHook_WithoutFrameworkFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "empty-app");
        var hookDir = Path.Combine(root, "cli", "hooks");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(hookDir);
        try
        {
            var hookDll = Path.Combine(hookDir, "UiPilot.StartupHook.dll");
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
            Assert.False(psi.Environment.ContainsKey(StartupHookLocator.FrameworkOverrideEnvVarName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyTo_SetsExplicitFrameworkOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "app");
        var hookDir = Path.Combine(root, "cli", "hooks");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(hookDir);
        try
        {
            var hookDll = Path.Combine(hookDir, "UiPilot.StartupHook.dll");
            File.WriteAllBytes(hookDll, Array.Empty<byte>());

            var psi = new System.Diagnostics.ProcessStartInfo();
            var applied = StartupHookLocator.ApplyTo(
                psi,
                appDir,
                uiFramework: "WinForms",
                useStartupHook: true,
                baseDirectory: Path.Combine(root, "cli"));

            Assert.Equal(hookDll, applied);
            Assert.Equal(hookDll, psi.Environment[StartupHookLocator.EnvVarName]);
            Assert.Equal("winforms", psi.Environment[StartupHookLocator.FrameworkOverrideEnvVarName]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyTo_RejectsUnknownFrameworkOverride()
    {
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var hookDir = Path.Combine(root, "cli", "hooks");
        Directory.CreateDirectory(hookDir);
        try
        {
            File.WriteAllBytes(Path.Combine(hookDir, "UiPilot.StartupHook.dll"), Array.Empty<byte>());
            var psi = new System.Diagnostics.ProcessStartInfo();
            Assert.Throws<ArgumentException>(() => StartupHookLocator.ApplyTo(
                psi,
                root,
                uiFramework: "unknown",
                useStartupHook: true,
                baseDirectory: Path.Combine(root, "cli")));
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

