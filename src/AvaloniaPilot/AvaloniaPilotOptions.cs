using System;

namespace AvaloniaPilot;

/// <summary>Configuration for <see cref="AvaloniaPilotHost.Start"/>.</summary>
public sealed class AvaloniaPilotOptions
{
    public bool Force { get; set; }
    public string? PipeName { get; set; }
    public string? Token { get; set; }
    public string? DiscoveryDirectory { get; set; }
    public bool? StartMinimized { get; set; }

    public const string EnableEnvVar = WpfPilot.PilotOptions.EnableEnvVar;
    public const string StartMinimizedEnvVar = WpfPilot.PilotOptions.StartMinimizedEnvVar;

    internal WpfPilot.PilotOptions ToPilotOptions() => new WpfPilot.PilotOptions
    {
        Force = Force,
        PipeName = PipeName,
        Token = Token,
        DiscoveryDirectory = DiscoveryDirectory,
        StartMinimized = StartMinimized,
        UiFramework = WpfPilot.UiFrameworks.Avalonia,
    };
}
