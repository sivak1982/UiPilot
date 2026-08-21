using System;

namespace UiPilot;

/// <summary>
/// Marks a public static method as an opt-in custom pilot tool. Reserved for domain-specific
/// actions; basic UI automation never requires attributes. After <c>PilotHost.Start()</c>,
/// matching methods on the entry assembly are registered automatically and appear in
/// <c>describe_app_tools</c> / <c>invoke_app_tool</c>.
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
