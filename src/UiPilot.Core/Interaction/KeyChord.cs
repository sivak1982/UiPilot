using System;
using System.Collections.Generic;
using System.Globalization;
using UiPilot.Tools;

namespace UiPilot.Interaction;

/// <summary>Modifier flags shared by every adapter's <c>press_keys</c> grammar.</summary>
[Flags]
public enum KeyModifier
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

/// <summary>
/// Framework-neutral parse of the agent <c>press_keys</c> grammar
/// (<c>ctrl+s</c>, <c>Enter</c>, or a literal string to type).
/// </summary>
public readonly struct KeyChord
{
    private KeyChord(KeyModifier modifiers, string keyToken, bool isPlainText)
    {
        Modifiers = modifiers;
        KeyToken = keyToken;
        IsPlainText = isPlainText;
    }

    public KeyModifier Modifiers { get; }
    public string KeyToken { get; }
    public bool IsPlainText { get; }
    public bool HasModifiers => Modifiers != KeyModifier.None;

    public static bool StartsWithModifier(string keys)
    {
        if (string.IsNullOrEmpty(keys)) return false;
        var first = keys.Split(new[] { '+' }, 2)[0];
        return TryParseModifier(first, out _);
    }

    public static KeyChord Parse(string keys)
    {
        if (string.IsNullOrEmpty(keys))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, "Keys cannot be empty.");

        if (keys.IndexOf('+', StringComparison.Ordinal) < 0)
        {
            if (TryCanonicalKey(keys, out var canonical))
                return new KeyChord(KeyModifier.None, canonical, isPlainText: false);
            return new KeyChord(KeyModifier.None, keys, isPlainText: true);
        }

        if (!StartsWithModifier(keys))
            return new KeyChord(KeyModifier.None, keys, isPlainText: true);

        var parts = SplitParts(keys);
        if (parts.Count < 2)
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Invalid key combination '{keys}'.");

        var modifiers = KeyModifier.None;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            if (!TryParseModifier(parts[i], out var flag))
                throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown modifier '{parts[i]}'.");
            modifiers |= flag;
        }

        if (!TryCanonicalKey(parts[parts.Count - 1], out var key))
            throw new PilotToolException(PilotErrorCodes.InvalidArgs, $"Unknown key '{parts[parts.Count - 1]}'.");

        return new KeyChord(modifiers, key, isPlainText: false);
    }

    public static bool TryCanonicalKey(string token, out string canonical)
    {
        token = NormalizeToken(token);
        canonical = "";
        if (token.Length == 1)
        {
            var ch = token[0];
            if (ch is >= 'A' and <= 'Z')
            {
                canonical = ch.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (ch is >= '0' and <= '9')
            {
                canonical = "D" + ch.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        switch (token)
        {
            case "ENTER":
            case "RETURN":
                canonical = "ENTER";
                return true;
            case "TAB":
                canonical = "TAB";
                return true;
            case "ESC":
            case "ESCAPE":
                canonical = "ESCAPE";
                return true;
            case "BACKSPACE":
            case "BKSP":
                canonical = "BACKSPACE";
                return true;
            case "DELETE":
            case "DEL":
                canonical = "DELETE";
                return true;
            case "LEFT":
            case "RIGHT":
            case "UP":
            case "DOWN":
            case "HOME":
            case "END":
            case "SPACE":
                canonical = token;
                return true;
            case "PAGEUP":
            case "PGUP":
                canonical = "PAGEUP";
                return true;
            case "PAGEDOWN":
            case "PGDN":
                canonical = "PAGEDOWN";
                return true;
        }

        if (token.Length is 2 or 3 && token[0] == 'F' &&
            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn) &&
            fn is >= 1 and <= 12)
        {
            canonical = "F" + fn.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    public static string NormalizeToken(string token) =>
        token.Trim().Replace(" ", string.Empty).ToUpperInvariant();

    public static bool TryParseModifier(string token, out KeyModifier modifier)
    {
        switch (NormalizeToken(token))
        {
            case "CTRL":
            case "CONTROL":
                modifier = KeyModifier.Control;
                return true;
            case "ALT":
                modifier = KeyModifier.Alt;
                return true;
            case "SHIFT":
                modifier = KeyModifier.Shift;
                return true;
            case "WIN":
            case "WINDOWS":
            case "META":
            case "CMD":
            case "COMMAND":
                modifier = KeyModifier.Meta;
                return true;
            default:
                modifier = KeyModifier.None;
                return false;
        }
    }

    private static List<string> SplitParts(string keys)
    {
        var raw = keys.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(raw.Length);
        foreach (var part in raw)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                parts.Add(trimmed);
        }
        return parts;
    }
}
