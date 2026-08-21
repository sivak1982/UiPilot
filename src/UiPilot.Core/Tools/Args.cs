using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UiPilot.Tools;

/// <summary>Small helpers for pulling typed values out of a request's <c>params</c> object.</summary>
internal static class Args
{
    public static string? GetString(this JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind != JsonValueKind.String)
            throw WrongType(name, "string", v.ValueKind);
        return v.GetString();
    }

    public static string GetRequiredString(this JsonElement args, string name)
    {
        var value = args.GetString(name);
        if (string.IsNullOrEmpty(value))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Missing required string argument '{name}'.");
        return value!;
    }

    public static int GetInt(this JsonElement args, string name, int fallback)
    {
        if (!TryGetProperty(args, name, out var v) || v.ValueKind == JsonValueKind.Null)
            return fallback;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
            throw WrongType(name, "integer", v.ValueKind);
        return i;
    }

    public static double? GetDouble(this JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d))
            throw WrongType(name, "number", v.ValueKind);
        return d;
    }

    public static bool GetBool(this JsonElement args, string name, bool fallback)
    {
        if (!TryGetProperty(args, name, out var v) || v.ValueKind == JsonValueKind.Null)
            return fallback;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        throw WrongType(name, "boolean", v.ValueKind);
    }

    public static IReadOnlyList<string>? GetStringList(this JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var v) ||
            v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (v.ValueKind != JsonValueKind.Array)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Argument '{name}' must be an array of strings.");

        var values = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Argument '{name}' must be an array of strings.");
            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static bool TryGetProperty(JsonElement args, string name, out JsonElement value)
    {
        value = default;
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value);
    }

    private static PilotToolException WrongType(string name, string expected, JsonValueKind actual) =>
        new(
            PilotErrorCodes.InvalidArgs,
            $"Argument '{name}' must be a {expected} (got {actual}).");
}
