using System.Text.Json.Serialization;

namespace WpfPilot.Media;

public sealed class ScreenshotData
{
    [JsonPropertyName("format")] public string Format { get; set; } = "png";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("base64")] public string Base64 { get; set; } = "";
}
