using System.Text.Json;

namespace UiPilot.Tools;

/// <summary>JSON Schema fragments for MCP <c>tools/list</c> input contracts.</summary>
internal static class ToolSchemas
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static JsonElement EmptyObject { get; } =
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }, Options);

    public static JsonElement Object(object properties, string[]? required = null)
    {
        if (required == null || required.Length == 0)
            return JsonSerializer.SerializeToElement(new { type = "object", properties }, Options);
        return JsonSerializer.SerializeToElement(new { type = "object", properties, required }, Options);
    }

    public static object String(string? description = null) =>
        description == null ? new { type = "string" } : new { type = "string", description };

    public static object Integer(string? description = null) =>
        description == null ? new { type = "integer" } : new { type = "integer", description };

    public static object Number(string? description = null) =>
        description == null ? new { type = "number" } : new { type = "number", description };

    public static object Boolean(string? description = null) =>
        description == null ? new { type = "boolean" } : new { type = "boolean", description };

    public static object StringArray(string? description = null) =>
        description == null
            ? new { type = "array", items = new { type = "string" } }
            : new { type = "array", items = new { type = "string" }, description };
}
