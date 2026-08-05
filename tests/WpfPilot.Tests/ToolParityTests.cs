using System.Reflection;
using WpfPilot.Cli.Tools;
using WpfPilot.Tools;
using Xunit;

namespace WpfPilot.Tests;

public class ToolParityTests
{
    [Fact]
    public void ForwardingTools_ExposeEveryBuiltInTool()
    {
        var mcpToolNames = typeof(ForwardingTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(GetMcpToolName)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var expected = ToolCatalog.BuiltInToolNames
            .Concat(new[] { "describe_app_tools", "invoke_app_tool" })
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(expected.Except(mcpToolNames, StringComparer.Ordinal));
        Assert.Empty(mcpToolNames.Except(expected, StringComparer.Ordinal));
    }

    private static string? GetMcpToolName(MethodInfo method)
    {
        var attribute = method.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().FullName == "ModelContextProtocol.Server.McpServerToolAttribute");
        return attribute?.GetType().GetProperty("Name")?.GetValue(attribute) as string;
    }
}
