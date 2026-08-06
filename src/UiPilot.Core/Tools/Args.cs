using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UiPilot.Tools;

/// <summary>Small helpers for pulling typed values out of a request's <c>params</c> object.</summary>
internal static class Args
{
    public static string? GetString(this JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
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
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.Number &&
            v.TryGetInt32(out var i))
            return i;
        return fallback;
    }

    public static double? GetDouble(this JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var v) &&
            v.ValueKind == JsonValueKind.Number &&
            v.TryGetDouble(out var d))
            return d;
        return null;
    }

    public static bool GetBool(this JsonElement args, string name, bool fallback)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString()?.Trim();
                if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "1", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "0", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }
        return fallback;
    }

    public static IReadOnlyList<string>? GetStringList(this JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var v) ||
            v.ValueKind == JsonValueKind.Null ||
            v.ValueKind == JsonValueKind.Undefined)
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
}
