using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WpfPilot.Inspection;

/// <summary>Agent-friendly summary of a single element. Always includes identity + bounds.</summary>
public sealed class ElementInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; }
    [JsonPropertyName("height")] public double Height { get; set; }

    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("visible")] public bool Visible { get; set; }
    [JsonPropertyName("childCount")] public int ChildCount { get; set; }

    /// <summary>Populated only by inspect_element when children are requested.</summary>
    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ElementInfo>? Children { get; set; }

    /// <summary>Optional framework-specific property snapshot requested by inspect_element.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Properties { get; set; }
}

public sealed class LayoutIssue
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("issue")] public string Issue { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}
