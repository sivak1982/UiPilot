using UiPilot.Inspection;
using Xunit;

namespace UiPilot.Tests;

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

    [Fact]
    public void Prune_RemovesCollectedHandles()
    {
        var registry = new ElementRegistry();
        var id = RegisterTransient(registry);

        ForceGc();
        registry.Prune();

        Assert.Null(registry.Resolve(id));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string RegisterTransient(ElementRegistry registry)
    {
        var node = new Node();
        return registry.GetOrAdd(node);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
