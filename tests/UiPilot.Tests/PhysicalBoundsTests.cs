using UiPilot.Inspection;
using Xunit;

namespace UiPilot.Tests;

public class PhysicalBoundsTests
{
    [Fact]
    public void SetFromScreenCorners_UsesPhysicalPixels()
    {
        var info = new ElementInfo();
        PhysicalBounds.SetFromScreenCorners(info, 100, 200, 250, 260);
        Assert.Equal(100, info.X);
        Assert.Equal(200, info.Y);
        Assert.Equal(150, info.Width);
        Assert.Equal(60, info.Height);
    }

    [Fact]
    public void SetPhysicalSizeOnly_LeavesOriginZero()
    {
        var info = new ElementInfo();
        PhysicalBounds.SetPhysicalSizeOnly(info, dipWidth: 80, dipHeight: 40, scaleX: 1.5, scaleY: 1.5);
        Assert.Equal(0, info.X);
        Assert.Equal(0, info.Y);
        Assert.Equal(120, info.Width);
        Assert.Equal(60, info.Height);
    }
}
