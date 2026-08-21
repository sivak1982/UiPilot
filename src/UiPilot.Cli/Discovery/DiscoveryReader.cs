using System.Globalization;
using System.Text.Json;
using UiPilot.Server;

namespace UiPilot.Client.Discovery;

/// <summary>Reads discovery files and filters out entries whose process is gone or stale.</summary>
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
            if (!IsAlive(info.Pid) || !IsTrusted(info))
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

    /// <summary>
    /// Require a live PID plus a usable pipe/token and a parseable StartedUtc so recycled PIDs
    /// with leftover discovery files are less likely to be trusted blindly.
    /// </summary>
    private static bool IsTrusted(DiscoveryInfo info)
    {
        if (info.Pid <= 0) return false;
        if (string.IsNullOrWhiteSpace(info.PipeName)) return false;
        if (string.IsNullOrWhiteSpace(info.Token)) return false;
        if (string.IsNullOrWhiteSpace(info.StartedUtc)) return false;
        return DateTime.TryParse(
            info.StartedUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out _);
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
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
