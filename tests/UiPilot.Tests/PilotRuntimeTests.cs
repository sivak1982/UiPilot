using UiPilot;
using UiPilot.Hosting;
using Xunit;

namespace UiPilot.Tests;

public class PilotRuntimeTests
{
    [Fact]
    public void IsEnabled_HonorsForceAndEnableEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable(PilotOptions.EnableEnvVar);

        try
        {
            Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, null);
            Assert.True(PilotRuntime.IsEnabled(new PilotOptions { Force = true }));

            Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, "1");
            Assert.True(PilotRuntime.IsEnabled(new PilotOptions()));

            Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, "0");
            Assert.Equal(PilotRuntime.IsEntryAssemblyDebugBuild(), PilotRuntime.IsEnabled(new PilotOptions()));

            Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, null);
            Assert.Equal(PilotRuntime.IsEntryAssemblyDebugBuild(), PilotRuntime.IsEnabled(new PilotOptions()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PilotOptions.EnableEnvVar, original);
        }
    }
}
