using System;
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
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = "";
    [JsonPropertyName("pipeName")] public string PipeName { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "";
    [JsonPropertyName("startedUtc")] public string StartedUtc { get; set; } = "";
    [JsonPropertyName("mainWindowTitle")] public string? MainWindowTitle { get; set; }

    /// <summary>UI stack hosting the in-process tools (<c>wpf</c>, <c>avalonia</c>, …).</summary>
    [JsonPropertyName("uiFramework")] public string? UiFramework { get; set; }
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
        var json = JsonSerializer.Serialize(info, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
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
