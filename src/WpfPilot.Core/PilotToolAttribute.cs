using System;

namespace WpfPilot;

/// <summary>
/// Marks a static method as an opt-in custom pilot tool. Reserved for domain-specific
/// actions; basic UI automation never requires attributes. Discovery/registration of these
/// is a post-MVP feature - the attribute exists so the contract is stable across WPF and Avalonia.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PilotToolAttribute : Attribute
{
    public PilotToolAttribute(string name) => Name = name;

    /// <summary>Tool name exposed to the agent (snake_case recommended).</summary>
    public string Name { get; }

    /// <summary>Human/agent-facing description.</summary>
    public string? Description { get; set; }
}

/// <summary>Back-compat alias for <see cref="PilotToolAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WpfPilotToolAttribute : Attribute
{
    public WpfPilotToolAttribute(string name) => Name = name;

    public string Name { get; }

    public string? Description { get; set; }
}
