using WpfPilot;
using WpfPilot.Hosting;
using Xunit;

namespace WpfPilot.Tests;

public class PilotRuntimeTests
{
    [Fact]
    public void IsEnabled_HonorsForceAndEnableEnvironmentVariables()
    {
        var originalPrimary = Environment.GetEnvironmentVariable(PilotOptions.EnableEnvVar);
        var originalLegacy = Environment.GetEnvironmentVariable(PilotOptions.LegacyEnableEnvVar);

        try
        {
            SetEnableEnv(null, null);
            Assert.True(PilotRuntime.IsEnabled(new PilotOptions { Force = true }));

            SetEnableEnv("1", null);
            Assert.True(PilotRuntime.IsEnabled(new PilotOptions()));

            SetEnableEnv(null, "1");
            Assert.True(PilotRuntime.IsEnabled(new PilotOptions()));

            SetEnableEnv("0", "0");
            Assert.Equal(PilotRuntime.IsEntryAssemblyDebugBuild(), PilotRuntime.IsEnabled(new PilotOptions()));

            SetEnableEnv(null, null);
            Assert.Equal(PilotRuntime.IsEntryAssemblyDebugBuild(), PilotRuntime.IsEnabled(new PilotOptions()));
        }
        finally
        {
            SetEnableEnv(originalPrimary, originalLegacy);
        }
    }

    private static void SetEnableEnv(string? primary, string? legacy)
    {
        Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, primary);
        Environment.SetEnvironmentVariable(PilotOptions.LegacyEnableEnvVar, legacy);
    }
}
