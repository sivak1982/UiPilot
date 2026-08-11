using UiPilot.Cli;
using UiPilot.Cli.Scenario;
using Xunit;

namespace UiPilot.Tests;

public class ScenarioRunnerTests
{
    [Fact]
    public async Task RunAsync_AllStepsPass_ReportsPassedAndWritesReportJson()
    {
        var doc = ScenarioParser.Parse(
            "name: sleep-only\nsteps:\n  - sleep: { ms: 5 }\n  - stop_all\n", fallbackName: "t");

        using var connection = new ConnectionManager();
        var runner = new ScenarioRunner(connection);

        var report = await runner.RunAsync(doc);

        Assert.True(report.Passed);
        Assert.Equal(2, report.Steps.Count);
        Assert.All(report.Steps, s => Assert.Equal(StepStatus.Passed, s.Status));
        Assert.Empty(report.FailureScreenshots);
        Assert.Null(report.FailedStep);

        var reportPath = Path.Combine(report.ArtifactsDirectory, "report.json");
        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public async Task RunAsync_StepFails_SkipsRemainingSteps()
    {
        var doc = ScenarioParser.Parse(
            """
            name: fail-fast
            steps:
              - sleep: { ms: 1 }
              - click: { query: Login, session: missing, timeoutMs: 50, pollMs: 10 }
              - sleep: { ms: 1 }
            """,
            fallbackName: "t");

        using var connection = new ConnectionManager();
        var runner = new ScenarioRunner(connection);

        var report = await runner.RunAsync(doc);

        Assert.False(report.Passed);
        Assert.Equal(StepStatus.Passed, report.Steps[0].Status);
        Assert.Equal(StepStatus.Failed, report.Steps[1].Status);
        Assert.Equal(StepStatus.Skipped, report.Steps[2].Status);
        Assert.Equal("Skipped: an earlier step failed.", report.Steps[2].Message);
        Assert.NotNull(report.FailedStep);
        Assert.Equal(2, report.FailedStep!.Index);
    }

    [Fact]
    public async Task RunAsync_UnknownSession_FailureMessageIncludesSessionName()
    {
        var doc = ScenarioParser.Parse(
            "name: t\nsteps:\n  - wait: { query: Anything, session: nope, timeoutMs: 10, pollMs: 5 }\n",
            fallbackName: "t");

        using var connection = new ConnectionManager();
        var report = await new ScenarioRunner(connection).RunAsync(doc);

        Assert.False(report.Passed);
        Assert.Contains("nope", report.Steps[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvokesOnStepCompleted_InOrder()
    {
        var doc = ScenarioParser.Parse(
            "name: t\nsteps:\n  - sleep: { ms: 1 }\n  - sleep: { ms: 1 }\n", fallbackName: "t");

        using var connection = new ConnectionManager();
        var seen = new List<int>();
        var runner = new ScenarioRunner(connection) { OnStepCompleted = s => seen.Add(s.Index) };

        await runner.RunAsync(doc);

        Assert.Equal(new[] { 1, 2 }, seen);
    }

    [Fact]
    public async Task RunAsync_KeepOpenFalse_DoesNotThrowWhenNoSessions()
    {
        var doc = ScenarioParser.Parse("name: t\nsteps:\n  - sleep: { ms: 1 }\n", fallbackName: "t");

        using var connection = new ConnectionManager();
        var report = await new ScenarioRunner(connection).RunAsync(doc);

        Assert.True(report.Passed);
        Assert.Empty(connection.ListSessions());
    }
}
