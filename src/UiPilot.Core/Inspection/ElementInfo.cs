using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UiPilot.Inspection;

/// <summary>Agent-friendly summary of a single element. Always includes identity + bounds.</summary>
public sealed class ElementInfo
{
    /// <summary>Session-scoped handle used by interaction and inspection calls.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>Framework control type.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    /// <summary>Framework or accessibility name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Stable automation id when the application provides one.</summary>
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    /// <summary>Visible or accessible text exposed by the element.</summary>
    [JsonPropertyName("text")] public string? Text { get; set; }

    /// <summary>Physical screen-space left coordinate.</summary>
    [JsonPropertyName("x")] public double X { get; set; }
    /// <summary>Physical screen-space top coordinate.</summary>
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>Physical screen-space width.</summary>
    [JsonPropertyName("width")] public double Width { get; set; }
    /// <summary>Physical screen-space height.</summary>
    [JsonPropertyName("height")] public double Height { get; set; }

    /// <summary>Whether the control can currently accept interaction.</summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    /// <summary>Whether the control is currently visible.</summary>
    [JsonPropertyName("visible")] public bool Visible { get; set; }
    /// <summary>Number of immediate visual children.</summary>
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

/// <summary>A clipping, overlap, or related issue found during layout analysis.</summary>
public sealed class LayoutIssue
{
    /// <summary>Session-scoped handle of the affected element.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>Framework control type.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    /// <summary>Framework or accessibility name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Machine-readable issue category.</summary>
    [JsonPropertyName("issue")] public string Issue { get; set; } = "";
    /// <summary>Human-readable explanation of the issue.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}
