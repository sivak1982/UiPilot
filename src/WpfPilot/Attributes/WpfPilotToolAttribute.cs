using System;

namespace WpfPilot;

/// <summary>
/// Marks a static method as an opt-in custom WpfPilot tool. Reserved for domain-specific
/// actions; basic UI automation never requires attributes. Discovery/registration of these
/// is a post-MVP feature - the attribute exists now so the contract is stable.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WpfPilotToolAttribute : Attribute
{
    public WpfPilotToolAttribute(string name) => Name = name;

    /// <summary>Tool name exposed to the agent (snake_case recommended).</summary>
    public string Name { get; }

    /// <summary>Human/agent-facing description.</summary>
    public string? Description { get; set; }
}
