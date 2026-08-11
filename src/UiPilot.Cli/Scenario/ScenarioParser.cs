using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace UiPilot.Cli.Scenario;

/// <summary>
/// Parses a scenario YAML file into a validated <see cref="ScenarioDocument"/>.
/// Supported file shape:
/// <code>
/// name: my-test
/// keepOpen: false
/// foreground: false   # true keeps the app being driven visible and in front
/// vars:
///   user: sysadmin
/// steps:
///   - start_app: { path: "${bin}\\App.exe", session: oi }
///   - type:  { query: "User Name", text: "${user}", session: oi }
///   - click: { query: Login, session: oi }
///   - expect_visible: { query: Initialize, session: oi }
/// </code>
/// Variables resolve in precedence order: caller overrides, file <c>vars</c>, environment.
/// Unknown verbs, missing required properties, and unresolved variables fail at parse time.
/// </summary>
public static class ScenarioParser
{
    private static readonly Regex VariablePattern = new(@"\$\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public static ScenarioDocument ParseFile(string path, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (!File.Exists(path))
            throw new ScenarioException($"Scenario file not found: {path}");
        return Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path), overrides);
    }

    public static ScenarioDocument Parse(
        string yaml,
        string fallbackName,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        object? root;
        try
        {
            root = new DeserializerBuilder().Build().Deserialize<object?>(yaml);
        }
        catch (Exception ex)
        {
            throw new ScenarioException($"Invalid YAML: {ex.Message}", ex);
        }

        if (root is not Dictionary<object, object?> map)
            throw new ScenarioException("Scenario root must be a mapping with at least a 'steps' list.");

        var name = GetScalar(map, "name") ?? fallbackName;
        var keepOpen = string.Equals(GetScalar(map, "keepOpen"), "true", StringComparison.OrdinalIgnoreCase);
        var foreground = string.Equals(GetScalar(map, "foreground"), "true", StringComparison.OrdinalIgnoreCase);
        var vars = ReadVars(map, overrides);

        if (!TryGet(map, "steps", out var stepsNode) || stepsNode is not List<object?> stepList)
            throw new ScenarioException("Scenario must contain a 'steps' list.");

        var steps = new List<ScenarioStep>(stepList.Count);
        for (var i = 0; i < stepList.Count; i++)
            steps.Add(ParseStep(stepList[i], i + 1, vars));

        return new ScenarioDocument
        {
            Name = Substitute(name, vars, context: "name"),
            KeepOpen = keepOpen,
            Foreground = foreground,
            Steps = steps,
        };
    }

    private static Dictionary<string, string> ReadVars(
        Dictionary<object, object?> map,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGet(map, "vars", out var varsNode))
        {
            if (varsNode is not Dictionary<object, object?> varsMap)
                throw new ScenarioException("'vars' must be a mapping of name: value.");
            foreach (var (key, value) in varsMap)
                vars[key?.ToString() ?? ""] = value?.ToString() ?? "";
        }

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                vars[key] = value;
        }

        return vars;
    }

    private static ScenarioStep ParseStep(object? node, int index, IReadOnlyDictionary<string, string> vars)
    {
        string verb;
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (node)
        {
            // "- stop_all" (bare verb)
            case string bare:
                verb = bare.Trim();
                break;

            // "- click: { query: Login }" or "- sleep: 500" (scalar shorthand)
            case Dictionary<object, object?> stepMap when stepMap.Count == 1:
            {
                var pair = stepMap.First();
                verb = pair.Key?.ToString()?.Trim()
                       ?? throw new ScenarioException($"Step {index}: empty verb.");

                switch (pair.Value)
                {
                    case null:
                        break;
                    case Dictionary<object, object?> propMap:
                        foreach (var (key, value) in propMap)
                        {
                            var propName = key?.ToString() ?? "";
                            if (value is Dictionary<object, object?> or List<object?>)
                                throw new ScenarioException(
                                    $"Step {index} ({verb}): property '{propName}' must be a scalar.");
                            props[propName] = value?.ToString() ?? "";
                        }
                        break;
                    default: // scalar shorthand
                        if (!ScenarioVerbs.ScalarProp.TryGetValue(verb, out var scalarProp))
                            throw new ScenarioException(
                                $"Step {index} ({verb}): scalar form is not supported; use a property mapping.");
                        props[scalarProp] = pair.Value.ToString() ?? "";
                        break;
                }
                break;
            }

            case Dictionary<object, object?> multi:
                throw new ScenarioException(
                    $"Step {index}: each step must have exactly one verb, found {multi.Count} keys " +
                    $"({string.Join(", ", multi.Keys.Select(k => k?.ToString()))}).");

            default:
                throw new ScenarioException($"Step {index}: unsupported step shape.");
        }

        if (!ScenarioVerbs.Required.TryGetValue(verb, out var requiredGroups))
            throw new ScenarioException(
                $"Step {index}: unknown verb '{verb}'. Known verbs: " +
                string.Join(", ", ScenarioVerbs.Required.Keys.OrderBy(k => k)) + ".");

        // Substitute variables in every property value.
        foreach (var key in props.Keys.ToList())
            props[key] = Substitute(props[key], vars, context: $"step {index} ({verb}).{key}");

        foreach (var group in requiredGroups)
        {
            if (!group.Any(props.ContainsKey))
                throw new ScenarioException(
                    $"Step {index} ({verb}): requires " +
                    (group.Length == 1 ? $"'{group[0]}'" : $"one of {string.Join("/", group.Select(g => $"'{g}'"))}") +
                    ".");
        }

        return new ScenarioStep { Index = index, Verb = verb.ToLowerInvariant(), Props = props };
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> vars, string context)
    {
        return VariablePattern.Replace(value, match =>
        {
            var key = match.Groups[1].Value;
            if (vars.TryGetValue(key, out var resolved))
                return resolved;
            var env = Environment.GetEnvironmentVariable(key);
            if (env is not null)
                return env;
            throw new ScenarioException(
                $"Unresolved variable '${{{key}}}' in {context}. Define it under 'vars', pass an override, or set the environment variable.");
        });
    }

    private static bool TryGet(Dictionary<object, object?> map, string key, out object? value)
    {
        foreach (var (k, v) in map)
        {
            if (string.Equals(k?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static string? GetScalar(Dictionary<object, object?> map, string key) =>
        TryGet(map, key, out var value) ? value?.ToString() : null;
}
