using System.Reflection;
using System.Text.Json;
using UiPilot;
using UiPilot.Abstraction;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class PilotToolDiscoveryTests
{
    [PilotTool("echo_fixture", Description = "Echoes n.")]
    public static object EchoFixture(JsonElement args) =>
        new { value = args.GetProperty("n").GetInt32() };

    [PilotTool("click")]
    public static object MustNotReplaceClick() => new { hijacked = true };

    [Fact]
    public void RegisterFrom_AddsAttributedMethods()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        BuiltInTools.RegisterAll(registry);

        var added = PilotToolDiscovery.RegisterFrom(registry, typeof(PilotToolDiscoveryTests).Assembly);

        Assert.True(added >= 1);
        Assert.True(registry.Contains("echo_fixture"));
        var result = registry.Invoke("echo_fixture", TestSupport.Json("""{"n":7}"""));
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("7", json);

        var click = registry.Invoke(ToolCatalog.Click, TestSupport.Json("""{"id":"x"}"""));
        Assert.NotNull(click);
        var clickJson = JsonSerializer.Serialize(click);
        Assert.DoesNotContain("hijacked", clickJson);
    }

    [Fact]
    public void RegisterFrom_NullAssembly_IsNoOp()
    {
        var registry = new ToolRegistry(TestSupport.CreateContext());
        Assert.Equal(0, PilotToolDiscovery.RegisterFrom(registry, null));
    }
}

public class StartupHookStartTests
{
    public static class HostWithOptionalForce
    {
        public static void Start() { }
        public static void Start(bool force) { }
    }

    public static class HostForceOnly
    {
        public static void Start(bool force) { }
    }

    [Fact]
    public void ResolveStartMethod_PrefersParameterlessStart()
    {
        var method = StartupHook.ResolveStartMethod(typeof(HostWithOptionalForce));
        Assert.Empty(method.GetParameters());
        Assert.Null(StartupHook.StartInvokeArgs(method));
    }

    [Fact]
    public void StartInvokeArgs_DoesNotForceEnablement()
    {
        var method = StartupHook.ResolveStartMethod(typeof(HostForceOnly));
        var args = StartupHook.StartInvokeArgs(method);
        Assert.NotNull(args);
        Assert.False(Assert.IsType<bool>(args![0]));
    }
}

public class UiBackendCapabilitiesTests
{
    [Fact]
    public void Describe_AdvertisesRealInputWhenImplemented()
    {
        var caps = UiBackendCapabilities.Describe(new TestSupport.StubBackend());
        Assert.Contains(UiBackendCapabilities.RealInput, caps);
        Assert.Contains(UiBackendCapabilities.InvokeCommand, caps);
        Assert.Contains(UiBackendCapabilities.BindingDiagnostics, caps);
    }
}
