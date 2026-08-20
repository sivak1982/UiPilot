using UiPilot.Cli.Status;
using Xunit;

namespace UiPilot.Tests.Status;

public sealed class OperationHubTests
{
    [Fact]
    public void StartAndSucceed_MovesOperationFromCurrentToRecent()
    {
        var hub = new OperationHub();
        var operation = hub.Start("click", "forwarding", "sim");

        Assert.Single(hub.Snapshot().Current);

        operation.Succeed();
        var snapshot = hub.Snapshot();

        Assert.Empty(snapshot.Current);
        var completed = Assert.Single(snapshot.Recent);
        Assert.Equal("click", completed.Name);
        Assert.Equal("forwarding", completed.Category);
        Assert.Equal("sim", completed.Session);
        Assert.Equal("succeeded", completed.Outcome);
        Assert.NotNull(completed.CompletedAt);
        Assert.NotNull(completed.DurationMs);
        Assert.Null(completed.ErrorCode);
    }

    [Fact]
    public void Fail_RecordsOnlySafeSummaryFields()
    {
        var hub = new OperationHub();
        var operation = hub.Start("start_app", "lifecycle", "oi");

        operation.Fail("not_found", "Operation failed.");
        var completed = Assert.Single(hub.Snapshot().Recent);

        Assert.Equal("failed", completed.Outcome);
        Assert.Equal("not_found", completed.ErrorCode);
        Assert.Equal("Operation failed.", completed.MessageSummary);
    }

    [Fact]
    public void RecentOperations_AreBounded()
    {
        var hub = new OperationHub(capacity: 2);
        foreach (var name in new[] { "one", "two", "three" })
            hub.Start(name, "test").Succeed();

        Assert.Equal(new[] { "two", "three" }, hub.Snapshot().Recent.Select(e => e.Name));
    }
}
