using UiPilot.Abstraction;
using UiPilot.Inspection;
using Xunit;

namespace UiPilot.Tests;

public class FindPagePagingTests
{
    [Fact]
    public void Slice_PagesWithoutMixingUnits()
    {
        var items = new[] { "a", "b", "c", "d" };
        var page = FindPagePaging.Slice(items, offset: 1, limit: 2, s => new ElementInfo { Id = s });
        Assert.Equal(2, page.Count);
        Assert.Equal(4, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal("b", page.Elements[0].Id);
        Assert.Equal("c", page.Elements[1].Id);
    }
}
