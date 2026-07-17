using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using WpfPilot.Cli.Discovery;
using Xunit;

namespace WpfPilot.Tests;

public class DiscoveryReaderTests
{
    [Fact]
    public void ListAlive_KeepsLiveProcess_DropsAndDeletesDeadOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wpfpilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var alivePid = Process.GetCurrentProcess().Id;
            const int deadPid = 2000000000; // implausibly high; not a running process

            WriteEntry(dir, alivePid, "wpfpilot.alive");
            var deadPath = WriteEntry(dir, deadPid, "wpfpilot.dead");

            var reader = new DiscoveryReader(dir);
            var alive = reader.ListAlive();

            Assert.Contains(alive, a => a.Pid == alivePid);
            Assert.DoesNotContain(alive, a => a.Pid == deadPid);
            Assert.False(File.Exists(deadPath)); // stale file cleaned up

            Assert.NotNull(reader.FindByPid(alivePid));
            Assert.Null(reader.FindByPid(deadPid));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string WriteEntry(string dir, int pid, string pipeName)
    {
        var info = new DiscoveryInfo
        {
            Pid = pid,
            ProcessName = "test",
            PipeName = pipeName,
            Token = "t",
            ProtocolVersion = "1.0",
            StartedUtc = DateTime.UtcNow.ToString("o"),
        };
        var path = Path.Combine(dir, pid + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(info));
        return path;
    }
}
