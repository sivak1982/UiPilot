using System;
using System.Text.Json;

namespace WpfPilot.Server;

/// <summary>
/// Minimal JSON-RPC-style envelope used on the named pipe. One JSON object per line.
/// Requests carry the auth <c>token</c> as a top-level member (both ends are ours).
/// </summary>
internal sealed class RpcRequest
{
    public JsonElement? Id { get; set; }
    public string Method { get; set; } = "";
    public string? Token { get; set; }
    public JsonElement Params { get; set; }

    public static bool TryParse(string line, out RpcRequest request, out string? error)
    {
        request = new RpcRequest();
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var id))
                request.Id = id.Clone();

            if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            {
                error = "Missing 'method'.";
                return false;
            }
            request.Method = method.GetString() ?? "";

            if (root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String)
                request.Token = token.GetString();

            request.Params = root.TryGetProperty("params", out var p) ? p.Clone() : default;
            return true;
        }
        catch (Exception ex)
        {
            error = "Invalid JSON: " + ex.Message;
            return false;
        }
    }
}

internal static class RpcCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int Unauthorized = -32001;
    public const int ToolError = -32002;
}

internal static class Rpc
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Result(JsonElement? id, object? result)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = IdValue(id),
            result,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string Error(JsonElement? id, int code, string message, object? data = null)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = IdValue(id),
            error = new { code, message, data },
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object? IdValue(JsonElement? id)
    {
        if (id == null)
            return null;
        var el = id.Value;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.GetInt64();
            case JsonValueKind.String:
                return el.GetString();
            default:
                return null;
        }
    }
}
