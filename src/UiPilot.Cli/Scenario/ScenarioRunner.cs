using System.Diagnostics;
using System.Text.Json;
using UiPilot.Tools;

namespace UiPilot.Cli.Scenario;

/// <summary>
/// Executes a <see cref="ScenarioDocument"/> against <see cref="ConnectionManager"/> fail-fast:
/// the first failed step marks the run FAILED, remaining steps are skipped, every attached pilot
/// session is screenshotted for evidence, and (unless <c>keepOpen</c>) all sessions are stopped.
/// Interaction verbs resolve their <c>query</c> via <c>wait_for_element</c> first, so scenarios
/// don't need explicit waits before each action.
/// </summary>
public sealed class ScenarioRunner
{
    private const int DefaultWaitMs = 10_000;
    private const int DefaultLogWaitMs = 60_000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ConnectionManager _connection;

    public ScenarioRunner(ConnectionManager connection) => _connection = connection;

    /// <summary>Optional per-step progress callback (index, verb, target, status, message).</summary>
    public Action<StepResult>? OnStepCompleted { get; set; }

    public async Task<ScenarioReport> RunAsync(ScenarioDocument document, CancellationToken ct = default)
    {
        var startedUtc = DateTime.UtcNow;
        var runWatch = Stopwatch.StartNew();
        var artifactsDir = CreateArtifactsDirectory(document.Name, startedUtc);
        var results = new List<StepResult>(document.Steps.Count);
        var screenshots = new List<string>();
        var failed = false;

        foreach (var step in document.Steps)
        {
            if (failed)
            {
                Record(results, new StepResult
                {
                    Index = step.Index,
                    Verb = step.Verb,
                    Target = step.TargetDescription,
                    Status = StepStatus.Skipped,
                    DurationMs = 0,
                    Message = "Skipped: an earlier step failed.",
                });
                continue;
            }

            var stepWatch = Stopwatch.StartNew();
            try
            {
                var message = await ExecuteStepAsync(step, artifactsDir, ct).ConfigureAwait(false);
                Record(results, new StepResult
                {
                    Index = step.Index,
                    Verb = step.Verb,
                    Target = step.TargetDescription,
                    Status = StepStatus.Passed,
                    DurationMs = stepWatch.ElapsedMilliseconds,
                    Message = message,
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed = true;
                Record(results, new StepResult
                {
                    Index = step.Index,
                    Verb = step.Verb,
                    Target = step.TargetDescription,
                    Status = StepStatus.Failed,
                    DurationMs = stepWatch.ElapsedMilliseconds,
                    Message = ex is ScenarioException or PilotCliException ? ex.Message : ex.ToString(),
                });

                screenshots.AddRange(await CaptureFailureScreenshotsAsync(artifactsDir, step.Index, ct).ConfigureAwait(false));
            }
        }

        if (!document.KeepOpen)
        {
            try { _connection.StopAll(); }
            catch { /* teardown must not change the verdict */ }
        }

        var report = new ScenarioReport
        {
            Name = document.Name,
            Passed = !failed,
            StartedUtc = startedUtc,
            DurationMs = runWatch.ElapsedMilliseconds,
            ArtifactsDirectory = artifactsDir,
            Steps = results,
            FailureScreenshots = screenshots,
        };

        try
        {
            File.WriteAllText(
                Path.Combine(artifactsDir, "report.json"),
                JsonSerializer.Serialize(report, Json));
        }
        catch { /* report file is best-effort; the in-memory report is authoritative */ }

        return report;
    }

    private void Record(List<StepResult> results, StepResult result)
    {
        results.Add(result);
        OnStepCompleted?.Invoke(result);
    }

    /// <summary>Runs one step; returns an optional informational message for the report.</summary>
    private async Task<string?> ExecuteStepAsync(ScenarioStep step, string artifactsDir, CancellationToken ct)
    {
        switch (step.Verb)
        {
            case ScenarioVerbs.StartApp:
            {
                var snapshot = await _connection.StartAppAsync(
                    step.Require("path"),
                    step.Get("session"),
                    step.Get("workingDirectory"),
                    step.GetBool("useStartupHook", true),
                    step.Get("uiFramework"),
                    ct).ConfigureAwait(false);
                return $"Started '{snapshot.ProcessName}' (pid {snapshot.Pid}) as session '{snapshot.Name}'.";
            }

            case ScenarioVerbs.StartProcess:
            {
                var snapshot = await _connection.StartProcessAsync(
                    step.Require("path"),
                    step.Get("session"),
                    step.Get("workingDirectory"),
                    step.Get("arguments"),
                    ct).ConfigureAwait(false);
                return $"Started process '{snapshot.ProcessName}' (pid {snapshot.Pid}) as session '{snapshot.Name}'.";
            }

            case ScenarioVerbs.Attach:
            {
                var pidRaw = step.Get("pid");
                var snapshot = await _connection.AttachAsync(
                    pidRaw is null ? null : int.Parse(pidRaw),
                    step.Get("processName"),
                    step.Get("uiFramework"),
                    step.Get("session"),
                    ct).ConfigureAwait(false);
                return $"Attached to '{snapshot.ProcessName}' (pid {snapshot.Pid}) as session '{snapshot.Name}'.";
            }

            case ScenarioVerbs.WaitForLog:
            {
                var result = await _connection.WaitForLogAsync(
                    step.Require("pathOrGlob"),
                    step.Require("pattern"),
                    step.GetInt("timeoutMs", DefaultLogWaitMs),
                    step.GetInt("pollMs", 200),
                    step.GetBool("fromEnd", false),
                    ct).ConfigureAwait(false);
                return $"Matched '{result.Match}' in {Path.GetFileName(result.Path)} after {result.ElapsedMs} ms.";
            }

            case ScenarioVerbs.Wait:
            case ScenarioVerbs.ExpectVisible:
            {
                var element = await ResolveElementAsync(step, ct).ConfigureAwait(false);
                return $"Element found (id {element}).";
            }

            case ScenarioVerbs.ExpectNotVisible:
                await ExpectNotVisibleAsync(step, ct).ConfigureAwait(false);
                return "No visible match.";

            case ScenarioVerbs.Click:
            {
                var id = await ResolveElementAsync(step, ct).ConfigureAwait(false);
                await SendAsync(ToolCatalog.Click, new { id }, step, ct).ConfigureAwait(false);
                return null;
            }

            case ScenarioVerbs.Type:
            {
                var id = await ResolveElementAsync(step, ct).ConfigureAwait(false);
                await SendAsync(ToolCatalog.TypeText, new { id, text = step.Require("text") }, step, ct).ConfigureAwait(false);
                return null;
            }

            case ScenarioVerbs.PressKeys:
            {
                string? id = step.Get("id");
                if (id is null && step.Get("query") is not null)
                    id = await ResolveElementAsync(step, ct).ConfigureAwait(false);
                await SendAsync(ToolCatalog.PressKeys, new { id, keys = step.Require("keys") }, step, ct).ConfigureAwait(false);
                return null;
            }

            case ScenarioVerbs.SelectItem:
            {
                var id = await ResolveElementAsync(step, ct).ConfigureAwait(false);
                var indexRaw = step.Get("index");
                await SendAsync(ToolCatalog.SelectItem, new
                {
                    id,
                    text = step.Get("text"),
                    index = indexRaw is null ? (int?)null : int.Parse(indexRaw),
                }, step, ct).ConfigureAwait(false);
                return null;
            }

            case ScenarioVerbs.Sleep:
                await Task.Delay(step.GetInt("ms", 0), ct).ConfigureAwait(false);
                return null;

            case ScenarioVerbs.Screenshot:
            {
                var label = step.Get("name") ?? $"step-{step.Index}";
                var path = await SaveScreenshotAsync(step.Get("session"), artifactsDir, label, ct).ConfigureAwait(false);
                return $"Saved {path}.";
            }

            case ScenarioVerbs.StopApp:
                _connection.StopApp(step.Get("session"));
                return null;

            case ScenarioVerbs.StopAll:
                _connection.StopAll();
                return null;

            default:
                throw new ScenarioException($"Step {step.Index}: unknown verb '{step.Verb}'.");
        }
    }

    /// <summary>
    /// Waits for the step's <c>query</c> (or takes its explicit <c>id</c>) and returns the element
    /// handle id. Wraps a not-found timeout in a scenario-friendly message.
    /// </summary>
    private async Task<string> ResolveElementAsync(ScenarioStep step, CancellationToken ct)
    {
        var explicitId = step.Get("id");
        if (explicitId is not null)
            return explicitId;

        var query = step.Require("query");
        var timeoutMs = step.GetInt("timeoutMs", DefaultWaitMs);

        JsonElement result;
        try
        {
            result = await SendAsync(ToolCatalog.WaitForElement, new
            {
                query,
                root = step.Get("root"),
                timeoutMs,
                pollMs = step.GetInt("pollMs", 200),
            }, step, ct).ConfigureAwait(false);
        }
        catch (PilotCliException ex)
        {
            throw new ScenarioException(
                $"Step {step.Index} ({step.Verb}): element '{query}' not found within {timeoutMs} ms" +
                (step.Get("session") is { } s ? $" in session '{s}'" : "") + $". {ex.Message}", ex);
        }

        if (result.TryGetProperty("elements", out var elements)
            && elements.ValueKind == JsonValueKind.Array
            && elements.GetArrayLength() > 0
            && elements[0].TryGetProperty("id", out var idProp)
            && idProp.GetString() is { Length: > 0 } id)
        {
            return id;
        }

        throw new ScenarioException(
            $"Step {step.Index} ({step.Verb}): element '{query}' not found within {timeoutMs} ms.");
    }

    private async Task ExpectNotVisibleAsync(ScenarioStep step, CancellationToken ct)
    {
        var query = step.Require("query");
        var timeoutMs = step.GetInt("timeoutMs", DefaultWaitMs);
        var pollMs = step.GetInt("pollMs", 200);
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var result = await SendAsync(ToolCatalog.FindElements, new { query, limit = 20 }, step, ct).ConfigureAwait(false);

            var anyVisible = false;
            if (result.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in elements.EnumerateArray())
                {
                    if (element.TryGetProperty("visible", out var visible) && visible.GetBoolean())
                    {
                        anyVisible = true;
                        break;
                    }
                }
            }

            if (!anyVisible)
                return;

            if (watch.ElapsedMilliseconds >= timeoutMs)
                throw new ScenarioException(
                    $"Step {step.Index} (expect_not_visible): '{query}' is still visible after {timeoutMs} ms.");

            await Task.Delay(pollMs, ct).ConfigureAwait(false);
        }
    }

    private Task<JsonElement> SendAsync(string method, object args, ScenarioStep step, CancellationToken ct) =>
        _connection.SendAsync(method, args, step.Get("session"), ct);

    private async Task<IReadOnlyList<string>> CaptureFailureScreenshotsAsync(
        string artifactsDir, int failedStepIndex, CancellationToken ct)
    {
        var saved = new List<string>();
        foreach (var session in _connection.ListSessions().Where(s => s.Kind == "pilot"))
        {
            try
            {
                var path = await SaveScreenshotAsync(
                    session.Name, artifactsDir, $"failure-step{failedStepIndex}-{session.Name}", ct).ConfigureAwait(false);
                saved.Add(path);
            }
            catch
            {
                // Evidence capture is best-effort; the step failure is already recorded.
            }
        }

        return saved;
    }

    private async Task<string> SaveScreenshotAsync(string? session, string artifactsDir, string label, CancellationToken ct)
    {
        var result = await _connection.SendAsync(ToolCatalog.Screenshot, new { }, session, ct).ConfigureAwait(false);
        var base64 = result.GetProperty("base64").GetString() ?? "";
        var safeLabel = string.Join("_", label.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(artifactsDir, safeLabel + ".png");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(base64), ct).ConfigureAwait(false);
        return path;
    }

    private static string CreateArtifactsDirectory(string scenarioName, DateTime startedUtc)
    {
        var safeName = string.Join("_", scenarioName.Split(Path.GetInvalidFileNameChars()));
        var dir = Path.Combine(
            Path.GetTempPath(), "uipilot", "runs",
            $"{safeName}-{startedUtc:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
