using System;

namespace UiPilot;

/// <summary>
/// Marks a static method as an opt-in custom pilot tool. Reserved for domain-specific
/// actions; basic UI automation never requires attributes. Discovery/registration of these
/// is not implemented yet — register handlers on <c>PilotHost.Tools</c> after <c>Start()</c>,
/// then use <c>describe_app_tools</c> / <c>invoke_app_tool</c>.
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
