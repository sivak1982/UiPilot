using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using UiPilot.Abstraction;
using UiPilot.Inspection;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

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

    [Fact]
    public void BuiltInToolCatalog_MatchesRegisteredTools()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());

        BuiltInTools.RegisterAll(registry);

        Assert.Equal(ToolCatalog.BuiltInToolNames, registry.Names);
    }

    [Fact]
    public void WaitForElement_TimesOutWhenNoMatchAppears()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        BuiltInTools.RegisterAll(registry);

        var ex = Assert.Throws<PilotToolException>(() =>
            registry.Invoke(ToolCatalog.WaitForElement, TestSupport.Json("""{"query":"missing","timeoutMs":1,"pollMs":1}""")));

        Assert.Equal(PilotErrorCodes.Timeout, ex.Code);
        Assert.NotNull(ex.Hint);
    }

    [Fact]
    public void WaitForElement_HonorsContextCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var context = TestSupport.CreateContext();
        context.CancellationToken = cts.Token;
        var registry = new ToolRegistry(context);
        BuiltInTools.RegisterAll(registry);

        var ex = Assert.Throws<PilotToolException>(() =>
            registry.Invoke(ToolCatalog.WaitForElement, TestSupport.Json("""{"query":"missing","timeoutMs":10000,"pollMs":10000}""")));

        Assert.Equal(PilotErrorCodes.Canceled, ex.Code);
    }

    [Fact]
    public void FindElements_ReturnsPageCountAndTotal()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext(new PagedBackend()));
        BuiltInTools.RegisterAll(registry);

        var result = registry.Invoke(ToolCatalog.FindElements, TestSupport.Json("""{"query":"item","limit":2,"offset":3}"""));
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        Assert.True(root.GetProperty("hasMore").GetBoolean());
        Assert.Equal(2, root.GetProperty("elements").GetArrayLength());
    }

    [Fact]
    public void MissingRequiredString_ThrowsInvalidArgs()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        BuiltInTools.RegisterAll(registry);

        var ex = Assert.Throws<PilotToolException>(() =>
            registry.Invoke(ToolCatalog.Click, TestSupport.Json("""{}""")));

        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public void FindElements_ForwardsExactMatchToBackend()
    {
        var backend = new RecordingBackend();
        var registry = new ToolRegistry(TestSupport.CreateContext(backend));
        BuiltInTools.RegisterAll(registry);

        registry.Invoke(ToolCatalog.FindElements, TestSupport.Json("""{"query":"Initialized","exact":true}"""));

        Assert.True(backend.LastExactMatch);
    }

    [Fact]
    public void FindElements_DefaultsToSubstringMatch()
    {
        var backend = new RecordingBackend();
        var registry = new ToolRegistry(TestSupport.CreateContext(backend));
        BuiltInTools.RegisterAll(registry);

        registry.Invoke(ToolCatalog.FindElements, TestSupport.Json("""{"query":"Initialized"}"""));

        Assert.False(backend.LastExactMatch);
    }

    [Fact]
    public void WaitForElement_ForwardsExactMatchToBackend()
    {
        var backend = new RecordingBackend();
        var registry = new ToolRegistry(TestSupport.CreateContext(backend));
        BuiltInTools.RegisterAll(registry);

        Assert.Throws<PilotToolException>(() => registry.Invoke(
            ToolCatalog.WaitForElement,
            TestSupport.Json("""{"query":"Initialized","exact":true,"timeoutMs":1,"pollMs":1}""")));

        Assert.True(backend.LastExactMatch);
    }

    private sealed class RecordingBackend : TestSupport.StubBackend
    {
        public bool? LastExactMatch { get; private set; }

        public override FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false)
        {
            LastExactMatch = exactMatch;
            return base.FindPage(query, limit, offset, rootId, exactMatch);
        }
    }

    private sealed class PagedBackend : TestSupport.StubBackend
    {
        public override FindPage FindPage(string? query, int limit, int offset, string? rootId, bool exactMatch = false)
        {
            return new FindPage
            {
                Elements = new[]
                {
                    new ElementInfo { Id = "e1", Type = "TextBlock" },
                    new ElementInfo { Id = "e2", Type = "TextBlock" },
                },
                Count = 2,
                Total = 5,
                HasMore = true,
                Offset = offset,
                Limit = limit,
            };
        }
    }
}
