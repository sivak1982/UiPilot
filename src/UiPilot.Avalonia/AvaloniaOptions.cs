using System;
using UiPilot.Inspection;
using UiPilot.Media;

namespace UiPilot.Avalonia;

/// <summary>Configuration for <see cref="PilotHost.Start"/>.</summary>
public sealed class AvaloniaOptions
{
    public bool Force { get; set; }
    public string? PipeName { get; set; }
    public string? Token { get; set; }
    public string? DiscoveryDirectory { get; set; }
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
        UiFramework = UiFrameworks.Avalonia,
    };
}
