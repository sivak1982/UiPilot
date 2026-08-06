namespace UiPilot.Cli;

public sealed class PilotCliException : Exception
{
    public PilotCliException(string code, string message, string? hint = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Hint = hint;
    }

    public string Code { get; }

    public string? Hint { get; }
}
