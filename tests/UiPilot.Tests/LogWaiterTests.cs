using UiPilot.Client;
using UiPilot.Client.Process;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class LogWaiterTests
{
    [Fact]
    public async Task WaitAsync_MatchesExistingFileContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "app.log");
        await File.WriteAllTextAsync(path, "boot\nStartup completed\ndone\n");

        try
        {
            var result = await LogWaiter.WaitAsync(path, "Startup completed", timeoutMs: 5_000, pollMs: 50);

            Assert.Equal(path, result.Path);
            Assert.Equal("Startup completed", result.Match);
            Assert.True(result.ElapsedMs >= 0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WaitAsync_MatchesNewestGlobFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var older = Path.Combine(dir, "old.log");
        var newer = Path.Combine(dir, "new.log");
        await File.WriteAllTextAsync(older, "nope");
        await File.WriteAllTextAsync(newer, "ready NOW");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        try
        {
            var result = await LogWaiter.WaitAsync(Path.Combine(dir, "*.log"), "ready NOW", timeoutMs: 5_000, pollMs: 50);
            Assert.Equal(Path.GetFullPath(newer), result.Path);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WaitAsync_WaitsForFileToAppear()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "late.log");

        try
        {
            var wait = LogWaiter.WaitAsync(path, "go", timeoutMs: 5_000, pollMs: 50);
            await Task.Delay(150);
            await File.WriteAllTextAsync(path, "go");

            var result = await wait;
            Assert.Equal("go", result.Match);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WaitAsync_FromEnd_IgnoresExistingMatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "stream.log");
        await File.WriteAllTextAsync(path, "Startup completed\n");

        try
        {
            var wait = LogWaiter.WaitAsync(path, "Startup completed", timeoutMs: 400, pollMs: 50, fromEnd: true);
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => wait);
            Assert.Contains("Timed out", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WaitAsync_InvalidPattern_ThrowsInvalidArgs()
    {
        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            LogWaiter.WaitAsync("x.log", "(", timeoutMs: 1000));

        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public async Task WaitAsync_MatchesFileAcrossWildcardDateFolder()
    {
        // Logs nested under a date-stamped subfolder, located via a glob with a
        // wildcard directory segment: Logs\Host\*\*.log.
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var dateDir = Path.Combine(root, "Host", "20260811");
        Directory.CreateDirectory(dateDir);
        var older = Path.Combine(root, "Host", "20260713");
        Directory.CreateDirectory(older);
        await File.WriteAllTextAsync(Path.Combine(older, "host-20260713.log"), "stale, no match");

        var logPath = Path.Combine(dateDir, "host-20260811.log");
        await File.WriteAllTextAsync(logPath, "boot\nStartup completed\n");

        try
        {
            var glob = Path.Combine(root, "Host", "*", "*.log");
            var result = await LogWaiter.WaitAsync(glob, "Startup completed", timeoutMs: 5_000, pollMs: 50);

            Assert.Equal(Path.GetFullPath(logPath), result.Path);
            Assert.Equal("Startup completed", result.Match);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveNewestFile_WildcardDirectorySegment_PicksNewestAcrossSubfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        var dayOne = Path.Combine(root, "20260713");
        var dayTwo = Path.Combine(root, "20260811");
        Directory.CreateDirectory(dayOne);
        Directory.CreateDirectory(dayTwo);
        var older = Path.Combine(dayOne, "a.log");
        var newer = Path.Combine(dayTwo, "b.log");
        File.WriteAllText(older, "old");
        File.WriteAllText(newer, "new");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        try
        {
            var resolved = LogWaiter.ResolveNewestFile(Path.Combine(root, "*", "*.log"));
            Assert.Equal(Path.GetFullPath(newer), resolved);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveNewestFile_Directory_ReturnsNewest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow);

        try
        {
            var resolved = LogWaiter.ResolveNewestFile(dir);
            Assert.Equal(Path.GetFullPath(b), resolved);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}

public class StartProcessTests
{
    [Fact]
    public async Task StartProcess_MissingPath_ReturnsNotFound()
    {
        using var manager = new ConnectionManager();
        var missing = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"), "Nope.exe");

        var ex = await Assert.ThrowsAsync<PilotCliException>(() =>
            manager.StartProcessAsync(missing, session: "supervisor"));

        Assert.Equal(PilotErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public async Task StartProcess_TracksProcessSession_AndStopKillsIt()
    {
        using var manager = new ConnectionManager();
        var shell = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";

        var snapshot = await manager.StartProcessAsync(
            shell,
            session: "helper",
            arguments: "/c ping 127.0.0.1 -n 30 >nul");

        Assert.Equal("helper", snapshot.Name);
        Assert.Equal("process", snapshot.Kind);
        Assert.True(snapshot.Pid > 0);
        Assert.True(snapshot.CanRestart);
        Assert.Contains(manager.ListSessions(), s => s.Name == "helper" && s.Kind == "process");

        var stopped = manager.StopApp("helper");
        Assert.NotNull(stopped);
        Assert.DoesNotContain(manager.ListSessions(), s => s.Name == "helper");

        await Task.Delay(200);
        try
        {
            using var lingering = System.Diagnostics.Process.GetProcessById(snapshot.Pid);
            Assert.True(lingering.HasExited);
        }
        catch (ArgumentException)
        {
            // PID already gone — expected after stop.
        }
    }

    [Fact]
    public async Task WaitForLog_ThroughManager_Matches()
    {
        using var manager = new ConnectionManager();
        var dir = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "svc.log");
        await File.WriteAllTextAsync(path, "line\nREADY\n");

        try
        {
            var result = await manager.WaitForLogAsync(path, "READY", timeoutMs: 3_000, pollMs: 50);
            Assert.Equal("READY", result.Match);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
