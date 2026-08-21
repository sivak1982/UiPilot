using ModelContextProtocol.Protocol;
using UiPilot.Cli.Status;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests.Status;

public sealed class OperationTelemetryTests
{
    [Fact]
    public void Run_RecordsToolErrorEnvelope()
    {
        var hub = new OperationHub();
        var telemetry = new OperationTelemetry(hub);
        var result = new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = """{"code":"not_found"}""" }],
        };

        Assert.Same(result, telemetry.Run("inspect", "forwarding", "app", () => result));
        var operation = Assert.Single(hub.Snapshot().Recent);
        Assert.Equal("failed", operation.Outcome);
        Assert.Equal(PilotErrorCodes.NotFound, operation.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_RecordsCancellationWithCanonicalCode()
    {
        var hub = new OperationHub();
        var telemetry = new OperationTelemetry(hub);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            telemetry.RunAsync("wait", "forwarding", null, () =>
                Task.FromException<CallToolResult>(new OperationCanceledException())));

        var operation = Assert.Single(hub.Snapshot().Recent);
        Assert.Equal(PilotErrorCodes.Canceled, operation.ErrorCode);
    }
}
