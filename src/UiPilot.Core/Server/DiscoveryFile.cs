using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UiPilot.Server;

/// <summary>
/// On-disk contract the out-of-process CLI reads to find and authenticate to a running app.
/// Written to <c>%TEMP%/uipilot\&lt;pid&gt;.json</c>.
/// </summary>
public sealed class DiscoveryInfo
{
    /// <summary>Operating-system process id.</summary>
    [JsonPropertyName("pid")] public int Pid { get; set; }
    /// <summary>Operating-system process name.</summary>
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
    /// <summary>Local named-pipe endpoint.</summary>
    [JsonPropertyName("pipeName")] public string PipeName { get; set; } = "";
    /// <summary>Per-process authentication token. Do not log or persist it elsewhere.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    /// <summary>UiPilot wire-protocol version.</summary>
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "";
    /// <summary>UTC timestamp at which the pilot host started.</summary>
    [JsonPropertyName("startedUtc")] public string StartedUtc { get; set; } = "";
    /// <summary>Current main-window title when one is available.</summary>
    [JsonPropertyName("mainWindowTitle")] public string? MainWindowTitle { get; set; }

    /// <summary>UI stack hosting the in-process tools (<c>wpf</c>, <c>avalonia</c>, …).</summary>
    [JsonPropertyName("uiFramework")] public string? UiFramework { get; set; }
    /// <summary>Optional backend features that callers can check before invoking a tool.</summary>
    [JsonPropertyName("capabilities")] public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
}

internal static class DiscoveryFile
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    public static string DefaultDirectory =>
        Path.Combine(Path.GetTempPath(), "uipilot");

    public static string Write(DiscoveryInfo info, string? directory)
    {
        var dir = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory!;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, info.Pid + ".json");
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(info, JsonOptions);
        try
        {
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* best-effort temp cleanup */ }
        }
    }

    public static FileStream? TryAcquireProcessLock(int pid, string? directory)
    {
        var dir = string.IsNullOrEmpty(directory) ? DefaultDirectory : directory!;
        Directory.CreateDirectory(dir);
        try
        {
            return new FileStream(
                Path.Combine(dir, pid + ".lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort; a stale file is harmless (the CLI validates the pid is alive).
        }
    }
}
