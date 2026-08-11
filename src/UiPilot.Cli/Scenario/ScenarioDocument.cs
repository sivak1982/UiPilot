namespace UiPilot.Cli.Scenario;

/// <summary>
/// A parsed, variable-resolved scenario file: an ordered list of steps executed
/// fail-fast by <see cref="ScenarioRunner"/>.
/// </summary>
public sealed class ScenarioDocument
{
    public required string Name { get; init; }

    /// <summary>When true, sessions are left running after the scenario finishes.</summary>
    public bool KeepOpen { get; init; }

    public required IReadOnlyList<ScenarioStep> Steps { get; init; }
}

/// <summary>One step: a verb plus scalar properties (already variable-substituted).</summary>
public sealed class ScenarioStep
{
    /// <summary>1-based position in the file, for reporting.</summary>
    public required int Index { get; init; }

    public required string Verb { get; init; }

    /// <summary>Scalar properties as written (YAML scalars arrive as strings).</summary>
    public required IReadOnlyDictionary<string, string> Props { get; init; }

    public string? Get(string key) => Props.TryGetValue(key, out var v) ? v : null;

    public string Require(string key) =>
        Get(key) ?? throw new ScenarioException(
            $"Step {Index} ({Verb}): missing required property '{key}'.");

    public int GetInt(string key, int fallback)
    {
        var raw = Get(key);
        if (raw is null)
            return fallback;
        return int.TryParse(raw, out var value)
            ? value
            : throw new ScenarioException($"Step {Index} ({Verb}): '{key}' must be an integer, got '{raw}'.");
    }

    public bool GetBool(string key, bool fallback)
    {
        var raw = Get(key);
        if (raw is null)
            return fallback;
        return bool.TryParse(raw, out var value)
            ? value
            : throw new ScenarioException($"Step {Index} ({Verb}): '{key}' must be true/false, got '{raw}'.");
    }

    /// <summary>Human-readable target for reports (query, path, pattern, keys...).</summary>
    public string TargetDescription =>
        Get("query") ?? Get("id") ?? Get("path") ?? Get("pathOrGlob")
        ?? Get("pattern") ?? Get("keys") ?? Get("session") ?? Get("ms") ?? "";
}

/// <summary>Scenario parse or validation failure (bad file, unknown verb, missing prop).</summary>
public sealed class ScenarioException : Exception
{
    public ScenarioException(string message) : base(message) { }

    public ScenarioException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The verbs a scenario may use and their parse-time requirements.</summary>
public static class ScenarioVerbs
{
    public const string StartApp = "start_app";
    public const string StartProcess = "start_process";
    public const string Attach = "attach";
    public const string WaitForLog = "wait_for_log";
    public const string Wait = "wait";
    public const string Click = "click";
    public const string Type = "type";
    public const string PressKeys = "press_keys";
    public const string SelectItem = "select_item";
    public const string ExpectVisible = "expect_visible";
    public const string ExpectNotVisible = "expect_not_visible";
    public const string Sleep = "sleep";
    public const string Screenshot = "screenshot";
    public const string StopApp = "stop_app";
    public const string StopAll = "stop_all";

    /// <summary>verb -> property groups; each group needs at least one present property.</summary>
    public static readonly IReadOnlyDictionary<string, string[][]> Required =
        new Dictionary<string, string[][]>(StringComparer.OrdinalIgnoreCase)
        {
            [StartApp] = new[] { new[] { "path" } },
            [StartProcess] = new[] { new[] { "path" } },
            [Attach] = Array.Empty<string[]>(),
            [WaitForLog] = new[] { new[] { "pathOrGlob" }, new[] { "pattern" } },
            [Wait] = new[] { new[] { "query" } },
            [Click] = new[] { new[] { "query", "id" } },
            [Type] = new[] { new[] { "query", "id" }, new[] { "text" } },
            [PressKeys] = new[] { new[] { "keys" } },
            [SelectItem] = new[] { new[] { "query", "id" }, new[] { "text", "index" } },
            [ExpectVisible] = new[] { new[] { "query" } },
            [ExpectNotVisible] = new[] { new[] { "query" } },
            [Sleep] = new[] { new[] { "ms" } },
            [Screenshot] = Array.Empty<string[]>(),
            [StopApp] = Array.Empty<string[]>(),
            [StopAll] = Array.Empty<string[]>(),
        };

    /// <summary>Default property used when a step is written as a scalar (e.g. <c>- sleep: 500</c>).</summary>
    public static readonly IReadOnlyDictionary<string, string> ScalarProp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Wait] = "query",
            [Click] = "query",
            [ExpectVisible] = "query",
            [ExpectNotVisible] = "query",
            [PressKeys] = "keys",
            [Sleep] = "ms",
            [Screenshot] = "session",
            [StopApp] = "session",
            [StartApp] = "path",
            [StartProcess] = "path",
        };
}
