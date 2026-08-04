using System.IO;
using System.Text.Json;
using WpfPilot.Server;
using Xunit;

namespace WpfPilot.Tests;

public class DiscoveryFileTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsAllFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wpfpilot-tests", Guid.NewGuid().ToString("N"));
        var info = new DiscoveryInfo
        {
            Pid = 4321,
            ProcessName = "SampleApp",
            PipeName = "wpfpilot.4321.abc",
            Token = "tok",
            ProtocolVersion = "1.0",
            StartedUtc = "2026-07-17T07:00:00.0000000Z",
            MainWindowTitle = "Title",
            UiFramework = "wpf",
        };

        var path = DiscoveryFile.Write(info, dir);

        try
        {
            Assert.True(File.Exists(path));
            Assert.Equal(Path.Combine(dir, "4321.json"), path);

            var reloaded = JsonSerializer.Deserialize<DiscoveryInfo>(File.ReadAllText(path));
            Assert.NotNull(reloaded);
            Assert.Equal(info.Pid, reloaded!.Pid);
            Assert.Equal(info.PipeName, reloaded.PipeName);
            Assert.Equal(info.Token, reloaded.Token);
            Assert.Equal(info.ProtocolVersion, reloaded.ProtocolVersion);
            Assert.Equal(info.MainWindowTitle, reloaded.MainWindowTitle);
            Assert.Equal(info.UiFramework, reloaded.UiFramework);

            DiscoveryFile.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
