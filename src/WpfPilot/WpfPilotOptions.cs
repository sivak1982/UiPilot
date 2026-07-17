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
    /// Environment variable that force-enables WpfPilot regardless of build configuration.
    /// </summary>
    public const string EnableEnvVar = "WPFPILOT_ENABLE";
}
