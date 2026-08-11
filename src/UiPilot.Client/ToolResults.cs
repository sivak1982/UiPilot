using System.Text.Json.Serialization;
using UiPilot.Inspection;

namespace UiPilot.Client;

/// <summary>A paged visual-tree query result returned by find_elements/wait_for_element.</summary>
public sealed class ElementPageResult
{
    [JsonPropertyName("elements")] public IReadOnlyList<ElementInfo> Elements { get; init; } = [];
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; init; }
    [JsonPropertyName("offset")] public int Offset { get; init; }
    [JsonPropertyName("limit")] public int Limit { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }

    /// <summary>Returns the only match, failing clearly when the query was empty or ambiguous.</summary>
    public ElementInfo Single() =>
        Elements.Count == 1
            ? Elements[0]
            : throw new InvalidOperationException(
                $"Expected one element, but the response contained {Elements.Count}.");
}

/// <summary>Top-level windows returned by list_windows.</summary>
public sealed class WindowListResult
{
    [JsonPropertyName("windows")] public IReadOnlyList<ElementInfo> Windows { get; init; } = [];
    [JsonPropertyName("session")] public string? Session { get; init; }
}

/// <summary>Detailed element inspection plus the session that produced the handle.</summary>
public sealed class ElementResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
    [JsonPropertyName("width")] public double Width { get; init; }
    [JsonPropertyName("height")] public double Height { get; init; }
    [JsonPropertyName("enabled")] public bool Enabled { get; init; }
    [JsonPropertyName("visible")] public bool Visible { get; init; }
    [JsonPropertyName("childCount")] public int ChildCount { get; init; }
    [JsonPropertyName("children")] public IReadOnlyList<ElementInfo>? Children { get; init; }
    [JsonPropertyName("properties")] public IReadOnlyDictionary<string, string?>? Properties { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }
}

/// <summary>Result of a synthetic input operation such as click, type, focus, or select.</summary>
public sealed class InteractionResult
{
    [JsonPropertyName("method")] public string Method { get; init; } = "";
    [JsonPropertyName("session")] public string? Session { get; init; }
}

/// <summary>Result returned by invoke_command.</summary>
public sealed class CommandResult
{
    [JsonPropertyName("result")] public string? Result { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class PointResult
{
    [JsonPropertyName("x")] public double X { get; init; }
    [JsonPropertyName("y")] public double Y { get; init; }
}

/// <summary>Actual screen route used for a real mouse drag.</summary>
public sealed class DragResult
{
    [JsonPropertyName("from")] public PointResult From { get; init; } = new();
    [JsonPropertyName("to")] public PointResult To { get; init; } = new();
    [JsonPropertyName("steps")] public int Steps { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }
}

/// <summary>PNG captured in-process. Save it with <see cref="SaveAsync"/> when an artifact is needed.</summary>
public sealed class ScreenshotResult
{
    [JsonPropertyName("base64")] public string Base64 { get; init; } = "";
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }

    public byte[] GetBytes() => Convert.FromBase64String(Base64);

    public Task SaveAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return File.WriteAllBytesAsync(fullPath, GetBytes(), ct);
    }
}

public sealed class WindowStateResult
{
    [JsonPropertyName("state")] public string State { get; init; } = "";
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class BindingErrorsResult
{
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("errors")] public IReadOnlyList<string> Errors { get; init; } = [];
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class LayoutResult
{
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("issues")] public IReadOnlyList<LayoutIssue> Issues { get; init; } = [];
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class HighlightResult
{
    [JsonPropertyName("highlighted")] public bool Highlighted { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class PingResult
{
    [JsonPropertyName("pong")] public bool Pong { get; init; }
    [JsonPropertyName("session")] public string? Session { get; init; }
}

public sealed class ToolDescription
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }
}

public sealed class ToolListResult
{
    [JsonPropertyName("tools")] public IReadOnlyList<ToolDescription> Tools { get; init; } = [];
    [JsonPropertyName("session")] public string? Session { get; init; }
}
