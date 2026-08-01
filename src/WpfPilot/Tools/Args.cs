using System.Text.Json;

namespace WpfPilot.Tools;

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
            throw new System.ArgumentException($"Missing required string argument '{name}'.");
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
        }
        return fallback;
    }
}
