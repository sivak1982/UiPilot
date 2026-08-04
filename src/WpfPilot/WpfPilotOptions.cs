using System;

namespace WpfPilot;

/// <summary>
/// Configuration for <see cref="WpfPilotHost.Start"/>. Every value has a safe default;
/// the common case is to pass nothing at all.
/// </summary>
public sealed class WpfPilotOptions
{
    /// <summary>
    /// Enable even in Release / when the DEBUG-and-env gate would otherwise say no.
    /// Off by default so shipping builds never expose an automation surface by accident.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Explicit pipe name. When null (default) a unique name is generated per process
    /// so multiple apps can run side by side without collisions.
    /// </summary>
    public string? PipeName { get; set; }

    /// <summary>
    /// Auth token required on every request. When null (default) a random token is
    /// generated and written to the discovery file for the CLI to read.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Directory for discovery files. Defaults to <c>%TEMP%\wpfpilot</c>.
    /// </summary>
    public string? DiscoveryDirectory { get; set; }

    /// <summary>
    /// Minimize the main window shortly after it first renders, so an agent driving the app keeps
    /// the app out of the way (the agent/IDE stays visible). Screenshots still work while minimized
    /// because they render the visual tree offscreen; use <c>bring_to_front</c> to show it on demand.
    /// Null (default) means "use the <c>WPFPILOT_START_MINIMIZED</c> / <c>UIPILOT_START_MINIMIZED</c> env var";
    /// set true/false to override.
    /// </summary>
    public bool? StartMinimized { get; set; }

    /// <summary>
    /// Environment variable that force-enables WpfPilot regardless of build configuration.
    /// Prefer <see cref="PilotOptions.EnableEnvVar"/> (<c>UIPILOT_ENABLE</c>); this legacy name is still honored.
    /// </summary>
    public const string EnableEnvVar = PilotOptions.LegacyEnableEnvVar;

    /// <summary>
    /// Environment variable that requests the app start minimized (set by the CLI edit loop).
    /// </summary>
    public const string StartMinimizedEnvVar = PilotOptions.LegacyStartMinimizedEnvVar;

    internal PilotOptions ToPilotOptions() => new PilotOptions
    {
        Force = Force,
        PipeName = PipeName,
        Token = Token,
        DiscoveryDirectory = DiscoveryDirectory,
        StartMinimized = StartMinimized,
        UiFramework = UiFrameworks.Wpf,
    };
}
