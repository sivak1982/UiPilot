using System;

namespace UiPilot.Tools;

public sealed class PilotToolException : Exception
{
    public PilotToolException(string code, string message, string? hint = null)
        : base(message)
    {
        Code = code;
        Hint = hint;
    }

    public string Code { get; }

    public string? Hint { get; }
}

public static class PilotErrorCodes
{
    public const string StaleElement = "stale_element";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string NotAttached = "not_attached";
    public const string InvalidArgs = "invalid_args";
    public const string Unsupported = "unsupported";
    public const string Platform = "platform_unsupported";
    public const string Timeout = "timeout";
    public const string Canceled = "canceled";
}
