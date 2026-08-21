using System;

namespace UiPilot.Inspection;

/// <summary>
/// Element bounds are always physical screen pixels. When a screen transform is
/// unavailable, convert DIP size with the given scale and leave X/Y at 0 rather
/// than mixing logical width/height with physical origin.
/// </summary>
public static class PhysicalBounds
{
    public static void SetFromScreenCorners(
        ElementInfo info, double originX, double originY, double cornerX, double cornerY)
    {
        info.X = originX;
        info.Y = originY;
        info.Width = Math.Abs(cornerX - originX);
        info.Height = Math.Abs(cornerY - originY);
    }

    public static void SetPhysicalSizeOnly(
        ElementInfo info, double dipWidth, double dipHeight, double scaleX, double scaleY)
    {
        info.Width = dipWidth * scaleX;
        info.Height = dipHeight * scaleY;
    }
}
