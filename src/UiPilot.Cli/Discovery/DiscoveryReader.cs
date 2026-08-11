using System.Text.Json;
using UiPilot.Server;

namespace UiPilot.Client.Discovery;

/// <summary>Reads discovery files and filters out entries whose process is gone.</summary>
public sealed class DiscoveryReader
{
    public string Directory { get; }

    public DiscoveryReader(string? directory = null)
    {
        Directory = string.IsNullOrEmpty(directory)
            ? Path.Combine(Path.GetTempPath(), "uipilot")
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
