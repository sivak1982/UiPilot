using System.Text.Json;
using UiPilot.Client;
using UiPilot.Cli.Status;
using UiPilot.Server;
using Xunit;

namespace UiPilot.Tests.Status;

public sealed class StatusModelTests
{
    [Fact]
    public void AppProjection_DropsDiscoveryTokenAndPipeName()
    {
        var app = StatusAppInfo.From(new DiscoveryInfo
        {
            Pid = 4242,
            ProcessName = "SampleApp",
            PipeName = "uipilot.4242.secret",
            Token = "super-secret-token",
            ProtocolVersion = "1.0",
            StartedUtc = "2026-08-20T00:00:00.0000000Z",
            MainWindowTitle = "Sample",
            UiFramework = "wpf",
        });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(app, JsonSerializerOptions.Web));

        Assert.Equal(4242, json.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal("wpf", json.RootElement.GetProperty("uiFramework").GetString());
        StatusTestSupport.AssertNoSensitiveFields(json.RootElement);
        Assert.DoesNotContain("super-secret-token", json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("uipilot.4242.secret", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SessionProjection_CopiesOnlyDisplayFields()
    {
        var session = StatusSessionInfo.From(new SessionSnapshot
        {
            Name = "sim",
            Kind = "pilot",
            IsActive = true,
            Pid = 4242,
            ProcessName = "SampleApp",
            MainWindowTitle = "Sample",
            UiFramework = "wpf",
            LaunchedByCli = true,
            CanRestart = true,
        });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(session, JsonSerializerOptions.Web));

        Assert.Equal("sim", json.RootElement.GetProperty("name").GetString());
        Assert.True(json.RootElement.GetProperty("canRestart").GetBoolean());
        StatusTestSupport.AssertNoSensitiveFields(json.RootElement);
    }

    [Fact]
    public void OperationEventSerialization_CarriesOnlySummaryFields()
    {
        var hub = new OperationHub();
        hub.Start("type_text", "forwarding", "sim").Fail("invalid_args", "Operation failed.");
        var message = StatusMessage.OperationUpdate(hub.Snapshot().Recent[0]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(message, JsonSerializerOptions.Web));

        var operation = json.RootElement.GetProperty("operation");
        Assert.Equal("type_text", operation.GetProperty("name").GetString());
        Assert.Equal("invalid_args", operation.GetProperty("errorCode").GetString());
        Assert.Equal("Operation failed.", operation.GetProperty("messageSummary").GetString());
        StatusTestSupport.AssertNoSensitiveFields(json.RootElement);
    }
}
