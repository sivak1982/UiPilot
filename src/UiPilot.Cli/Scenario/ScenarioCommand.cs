namespace UiPilot.Cli.Scenario;

/// <summary>
/// Console entry point for <c>uipilot run &lt;file-or-folder&gt; [--var name=value ...]</c>.
/// Runs one scenario file, or every <c>*.yaml</c>/<c>*.yml</c> in a folder, printing per-step
/// progress and a final summary. Exit code 0 = all passed, 1 = failure, 2 = usage/parse error.
/// </summary>
public static class ScenarioCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        string? target = null;
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--var" or "-v")
            {
                if (i + 1 >= args.Length || args[i + 1].IndexOf('=') <= 0)
                {
                    Console.Error.WriteLine("Usage: --var name=value");
                    return 2;
                }

                var pair = args[++i];
                var eq = pair.IndexOf('=');
                overrides[pair[..eq]] = pair[(eq + 1)..];
            }
            else if (target is null)
            {
                target = args[i];
            }
            else
            {
                Console.Error.WriteLine($"Unexpected argument: {args[i]}");
                return 2;
            }
        }

        if (target is null)
        {
            Console.Error.WriteLine("Usage: uipilot run <scenario.yaml | folder> [--var name=value ...]");
            return 2;
        }

        string[] files;
        if (Directory.Exists(target))
        {
            files = Directory.EnumerateFiles(target, "*.yaml")
                .Concat(Directory.EnumerateFiles(target, "*.yml"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                Console.Error.WriteLine($"No *.yaml/*.yml scenario files found in {target}");
                return 2;
            }
        }
        else if (File.Exists(target))
        {
            files = new[] { target };
        }
        else
        {
            Console.Error.WriteLine($"Scenario path not found: {target}");
            return 2;
        }

        var anyFailed = false;
        foreach (var file in files)
        {
            var passed = await RunOneAsync(file, overrides, ct).ConfigureAwait(false);
            anyFailed |= !passed;
        }

        return anyFailed ? 1 : 0;
    }

    private static async Task<bool> RunOneAsync(
        string file, IReadOnlyDictionary<string, string> overrides, CancellationToken ct)
    {
        ScenarioDocument document;
        try
        {
            document = ScenarioParser.ParseFile(file, overrides);
        }
        catch (ScenarioException ex)
        {
            Console.Error.WriteLine($"PARSE ERROR  {Path.GetFileName(file)}: {ex.Message}");
            return false;
        }

        Console.WriteLine($"=== {document.Name} ({document.Steps.Count} steps) ===");

        using var connection = new ConnectionManager();
        var runner = new ScenarioRunner(connection)
        {
            OnStepCompleted = step =>
            {
                var mark = step.Status switch
                {
                    StepStatus.Passed => "PASS",
                    StepStatus.Failed => "FAIL",
                    _ => "SKIP",
                };
                var target = string.IsNullOrEmpty(step.Target) ? "" : $" \"{step.Target}\"";
                var detail = string.IsNullOrEmpty(step.Message) ? "" : $"  {step.Message}";
                Console.WriteLine($"  [{mark}] {step.Index,2}. {step.Verb}{target} ({step.DurationMs} ms){detail}");
            },
        };

        var report = await runner.RunAsync(document, ct).ConfigureAwait(false);

        Console.WriteLine($"--- {(report.Passed ? "PASSED" : "FAILED")}: {document.Name} in {report.DurationMs} ms ---");
        Console.WriteLine($"    Report: {Path.Combine(report.ArtifactsDirectory, "report.json")}");
        foreach (var shot in report.FailureScreenshots)
            Console.WriteLine($"    Screenshot: {shot}");
        Console.WriteLine();

        return report.Passed;
    }
}
