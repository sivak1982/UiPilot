using Xunit;

namespace UiPilot.Tests;

/// <summary>Reports Windows-only tests as skipped instead of passing without assertions.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Requires Windows desktop APIs.";
    }
}
