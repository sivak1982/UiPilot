using System;

namespace UiPilot;

/// <summary>
/// Shared configuration for in-process pilot hosts (WPF and Avalonia). Framework packages
/// expose typed options wrappers that map onto this.
/// </summary>
public sealed class PilotOptions
{
    /// <summary>Enable even in Release / when the DEBUG-and-env gate would otherwise say no.</summary>
    public bool Force { get; set; }

    /// <summary>Explicit pipe name. When null, a unique name is generated per process.</summary>
    public string? PipeName { get; set; }

    /// <summary>Auth token. When null, a random token is generated and written to discovery.</summary>
    public string? Token { get; set; }

    /// <summary>Directory for discovery files. Defaults to <c>%TEMP%/uipilot</c>.</summary>
    public string? DiscoveryDirectory { get; set; }

    /// <summary>
    /// Minimize the main window shortly after first render. Null means "use env var".
    /// </summary>
    public bool? StartMinimized { get; set; }

    /// <summary>
    /// UI framework label written into the discovery file (<c>wpf</c>, <c>avalonia</c>, …).
    /// </summary>
    public string UiFramework { get; set; } = UiFrameworks.Wpf;

    /// <summary>Environment variable that force-enables UiPilot regardless of build configuration.</summary>
    public const string EnableEnvVar = "UIPILOT_ENABLE";

    /// <summary>Environment variable that requests the app start minimized (set by the CLI edit loop).</summary>
    public const string StartMinimizedEnvVar = "UIPILOT_START_MINIMIZED";

    public static bool IsEnableEnvSet() =>
        IsTruthy(Environment.GetEnvironmentVariable(EnableEnvVar));

    public static bool IsStartMinimizedEnvSet() =>
        IsTruthy(Environment.GetEnvironmentVariable(StartMinimizedEnvVar));

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);
}

/// <summary>Known values for <see cref="PilotOptions.UiFramework"/> / discovery <c>uiFramework</c>.</summary>
public static class UiFrameworks
{
    public const string Wpf = "wpf";
    public const string Avalonia = "avalonia";
}
