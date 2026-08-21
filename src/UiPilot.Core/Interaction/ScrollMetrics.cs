using System;

namespace UiPilot.Interaction;

/// <summary>
/// Shared scroll contract: dx/dy are wheel lines. One line is WheelDeltaPerLine
/// (Windows WHEEL_DELTA). Both axes are independent; a non-zero dy must not drop dx.
/// </summary>
public static class ScrollMetrics
{
    public const int WheelDeltaPerLine = 120;

    public static int ToWheelDelta(double lines)
    {
        if (lines == 0) return 0;
        return (int)Math.Round(lines * WheelDeltaPerLine, MidpointRounding.AwayFromZero);
    }

    public static (int Vertical, int Horizontal) Axes(double dx, double dy) =>
        (ToWheelDelta(dy), ToWheelDelta(dx));
}
