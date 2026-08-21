using UiPilot;
using UiPilot.Hosting;
using UiPilot.Tools;
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

    [Fact]
    public void Start_RollsBackServerAndState_WhenDiscoverySetupFails()
    {
        var runtime = new PilotRuntime();
        var discoveryDirectory = Path.Combine(
            Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(discoveryDirectory);
        try
        {
            Assert.Throws<InvalidOperationException>(() => runtime.Start(
                new PilotOptions { Force = true, DiscoveryDirectory = discoveryDirectory },
                new TestSupport.StubBackend(),
                func => func(),
                () => throw new InvalidOperationException("title failed"),
                _ => { }));

            Assert.False(runtime.IsRunning);
            Assert.Null(runtime.Tools);
            Assert.Empty(Directory.GetFiles(discoveryDirectory));
        }
        finally
        {
            runtime.Dispose();
            Directory.Delete(discoveryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Stop_WaitsForInFlightToolBeforeBackendShutdown()
    {
        var runtime = new PilotRuntime();
        var backend = new ShutdownTrackingBackend();
        var discoveryDirectory = Path.Combine(
            Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(discoveryDirectory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            Assert.True(runtime.Start(
                new PilotOptions { Force = true, DiscoveryDirectory = discoveryDirectory },
                backend,
                func => func(),
                () => "test",
                _ => { }));

            runtime.Tools!.Register("blocking", "", (_, _) =>
            {
                entered.Set();
                release.Wait();
                return null;
            });
            var invoke = Task.Run(() => runtime.Tools!.Invoke(
                "blocking", TestSupport.Json("{}")));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

            var stop = Task.Run(() => runtime.Stop());
            await Task.Delay(50);
            Assert.False(backend.ShutdownCalled);

            release.Set();
            await Task.WhenAll(invoke, stop);
            Assert.True(backend.ShutdownCalled);
        }
        finally
        {
            release.Set();
            runtime.Dispose();
            Directory.Delete(discoveryDirectory, recursive: true);
        }
    }

    private sealed class ShutdownTrackingBackend : TestSupport.StubBackend
    {
        public bool ShutdownCalled { get; private set; }

        public override void Shutdown() => ShutdownCalled = true;
    }
}
