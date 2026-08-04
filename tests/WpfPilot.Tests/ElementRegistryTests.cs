using WpfPilot.Inspection;
using Xunit;

namespace WpfPilot.Tests;

public class ElementRegistryTests
{
    private sealed class Node { }

    [Fact]
    public void GetOrAdd_ReturnsStableIdForSameObject()
    {
        var registry = new ElementRegistry();
        var node = new Node();

        var first = registry.GetOrAdd(node);
        var second = registry.GetOrAdd(node);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetOrAdd_DistinctObjects_GetDistinctIds()
    {
        var registry = new ElementRegistry();
        var a = registry.GetOrAdd(new Node());
        var b = registry.GetOrAdd(new Node());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Resolve_ReturnsSameInstance()
    {
        var registry = new ElementRegistry();
        var node = new Node();
        var id = registry.GetOrAdd(node);

        Assert.Same(node, registry.Resolve(id));
    }

    [Fact]
    public void Resolve_UnknownOrNull_ReturnsNull()
    {
        var registry = new ElementRegistry();
        Assert.Null(registry.Resolve("does-not-exist"));
        Assert.Null(registry.Resolve(null));
    }
}
