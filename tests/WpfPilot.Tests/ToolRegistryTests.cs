using System.Collections.Generic;
using System.Text.Json;
using WpfPilot.Tools;
using Xunit;

namespace WpfPilot.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void Register_Invoke_PassesArgsAndReturnsResult()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        registry.Register("double_it", "doubles n", (_, args) => args.GetProperty("n").GetInt32() * 2);

        Assert.True(registry.Contains("double_it"));
        var result = registry.Invoke("double_it", TestSupport.Json("""{"n":21}"""));
        Assert.Equal(42, result);
    }

    [Fact]
    public void Contains_IsCaseInsensitive()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        registry.Register("Ping", "", (_, _) => null);
        Assert.True(registry.Contains("ping"));
        Assert.True(registry.Contains("PING"));
    }

    [Fact]
    public void Invoke_UnknownTool_Throws()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        Assert.Throws<KeyNotFoundException>(() => registry.Invoke("nope", default));
    }

    [Fact]
    public void Describe_ListsRegisteredTools()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        registry.Register("a", "first", (_, _) => null);
        registry.Register("b", "second", (_, _) => null);

        var json = JsonSerializer.Serialize(registry.Describe());
        using var doc = JsonDocument.Parse(json);
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
    }
}
