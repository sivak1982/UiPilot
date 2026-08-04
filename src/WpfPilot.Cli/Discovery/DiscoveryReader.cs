using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfPilot.Cli.Discovery;

public sealed class DiscoveryInfo
{
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("pipeName")] public string PipeName { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "";
    [JsonPropertyName("startedUtc")] public string StartedUtc { get; set; } = "";
    [JsonPropertyName("mainWindowTitle")] public string? MainWindowTitle { get; set; }
    [JsonPropertyName("uiFramework")] public string? UiFramework { get; set; }
}

/// <summary>Reads discovery files and filters out entries whose process is gone.</summary>
public sealed class DiscoveryReader
{
    public string Directory { get; }

    public DiscoveryReader(string? directory = null)
    {
        Directory = string.IsNullOrEmpty(directory)
            ? Path.Combine(Path.GetTempPath(), "wpfpilot")
            : directory!;
    }

    public IReadOnlyList<DiscoveryInfo> ListAlive()
    {
        var result = new List<DiscoveryInfo>();
        if (!System.IO.Directory.Exists(Directory)) return result;

        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            var info = TryRead(file);
            if (info == null) continue;
            if (!IsAlive(info.Pid))
            {
                TryDelete(file);
                continue;
            }
            result.Add(info);
        }
        return result;
    }

    public DiscoveryInfo? FindByPid(int pid)
    {
        foreach (var info in ListAlive())
            if (info.Pid == pid) return info;
        return null;
    }

    private static DiscoveryInfo? TryRead(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<DiscoveryInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var _ = System.Diagnostics.Process.GetProcessById(pid);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* ignore */ }
    }
}
