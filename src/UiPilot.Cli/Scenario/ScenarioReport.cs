using System.Text.Json.Serialization;

namespace UiPilot.Cli.Scenario;

public enum StepStatus
{
    Passed,
    Failed,
    Skipped,
}

/// <summary>Outcome of one executed (or skipped) scenario step.</summary>
public sealed class StepResult
{
    [JsonPropertyName("index")] public required int Index { get; init; }
    [JsonPropertyName("verb")] public required string Verb { get; init; }
    [JsonPropertyName("target")] public required string Target { get; init; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter<StepStatus>))]
    public required StepStatus Status { get; init; }

    [JsonPropertyName("durationMs")] public required long DurationMs { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}

/// <summary>
/// Full result of a scenario run: overall verdict, per-step outcomes, and failure artifacts.
/// Serialized to <c>report.json</c> inside <see cref="ArtifactsDirectory"/>.
/// </summary>
public sealed class ScenarioReport
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("passed")] public required bool Passed { get; init; }
    [JsonPropertyName("startedUtc")] public required DateTime StartedUtc { get; init; }
    [JsonPropertyName("durationMs")] public required long DurationMs { get; init; }
    [JsonPropertyName("artifactsDirectory")] public required string ArtifactsDirectory { get; init; }
    [JsonPropertyName("steps")] public required IReadOnlyList<StepResult> Steps { get; init; }

    /// <summary>Screenshot PNGs captured (per pilot session) when a step failed.</summary>
    [JsonPropertyName("failureScreenshots")]
    public required IReadOnlyList<string> FailureScreenshots { get; init; }

    [JsonPropertyName("failedStep")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StepResult? FailedStep => Steps.FirstOrDefault(s => s.Status == StepStatus.Failed);
}
