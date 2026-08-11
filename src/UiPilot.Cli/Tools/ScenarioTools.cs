using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UiPilot.Cli.Scenario;

namespace UiPilot.Cli.Tools;

/// <summary>
/// MCP tool that runs a YAML scenario file through <see cref="ScenarioRunner"/> and returns the
/// full report, so an agent (or a human via any MCP client) can execute deterministic UI test
/// scenarios and inspect pass/fail per step.
/// </summary>
[McpServerToolType]
public sealed class ScenarioTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConnectionManager _connection;

    public ScenarioTools(ConnectionManager connection) => _connection = connection;

    [McpServerTool(Name = "run_scenario")]
    [Description("Run a YAML UI-test scenario file deterministically (start apps, click, type, expect) and return a pass/fail report with per-step results and failure screenshots. Scenario docs: docs/08-scenarios.md.")]
    public async Task<CallToolResult> RunScenario(
        [Description("Path to the scenario .yaml file.")] string path,
        [Description("Optional JSON object of variable overrides, e.g. {\"user\":\"sysadmin\"}.")] string? varsJson = null,
        CancellationToken ct = default)
    {
        Dictionary<string, string>? overrides = null;
        if (!string.IsNullOrWhiteSpace(varsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(varsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Err("invalid_args", "varsJson must be a JSON object.");
                overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    overrides[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.GetRawText();
            }
            catch (JsonException ex)
            {
                return Err("invalid_args", "varsJson must be valid JSON.", ex.Message);
            }
        }

        ScenarioDocument document;
        try
        {
            document = ScenarioParser.ParseFile(path, overrides);
        }
        catch (ScenarioException ex)
        {
            return Err("scenario_parse", ex.Message);
        }

        var report = await new ScenarioRunner(_connection).RunAsync(document, ct).ConfigureAwait(false);

        return new CallToolResult
        {
            IsError = !report.Passed,
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = JsonSerializer.Serialize(report, Json) },
            },
        };
    }

    private static CallToolResult Err(string code, string message, string? hint = null) => new()
    {
        IsError = true,
        Content = new List<ContentBlock>
        {
            new TextContentBlock
            {
                Text = JsonSerializer.Serialize(new { error = true, code, message, hint }, Json),
            },
        },
    };
}
