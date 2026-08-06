using System;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Wpf;

/// <summary>
/// Configuration for <see cref="PilotHost.Start"/>. Every value has a safe default;
/// the common case is to pass nothing at all.
/// </summary>
public sealed class WpfOptions
{
    public bool Force { get; set; }
    public string? PipeName { get; set; }
    public string? Token { get; set; }
    public string? DiscoveryDirectory { get; set; }

    /// <summary>
    /// Minimize the main window shortly after it first renders.
    /// Null means use <c>UIPILOT_START_MINIMIZED</c>.
    /// </summary>
    public bool? StartMinimized { get; set; }

    public const string EnableEnvVar = PilotOptions.EnableEnvVar;
    public const string StartMinimizedEnvVar = PilotOptions.StartMinimizedEnvVar;

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
