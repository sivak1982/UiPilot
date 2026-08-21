using UiPilot.Interaction;
using Xunit;

namespace UiPilot.Tests;

public class ScrollMetricsTests
{
    [Fact]
    public void Axes_KeepHorizontalWhenVerticalIsAlsoSet()
    {
        var (vertical, horizontal) = ScrollMetrics.Axes(dx: 2, dy: -1);
        Assert.Equal(-ScrollMetrics.WheelDeltaPerLine, vertical);
        Assert.Equal(2 * ScrollMetrics.WheelDeltaPerLine, horizontal);
    }

    [Fact]
    public void ToWheelDelta_ZeroStaysZero()
    {
        Assert.Equal(0, ScrollMetrics.ToWheelDelta(0));
    }
}
