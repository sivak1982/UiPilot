using UiPilot.Cli.Scenario;
using Xunit;

namespace UiPilot.Tests;

public class ScenarioParserTests
{
    [Fact]
    public void Parse_MinimalDocument_ResolvesNameAndSteps()
    {
        var doc = ScenarioParser.Parse(
            """
            name: my-test
            steps:
              - click: { query: Login, session: oi }
              - sleep: { ms: 250 }
            """,
            fallbackName: "fallback");

        Assert.Equal("my-test", doc.Name);
        Assert.False(doc.KeepOpen);
        Assert.Equal(2, doc.Steps.Count);
        Assert.Equal(ScenarioVerbs.Click, doc.Steps[0].Verb);
        Assert.Equal("Login", doc.Steps[0].Get("query"));
        Assert.Equal("oi", doc.Steps[0].Get("session"));
        Assert.Equal(1, doc.Steps[0].Index);
        Assert.Equal(250, doc.Steps[1].GetInt("ms", -1));
    }

    [Fact]
    public void Parse_ExactFlag_IsReadableAsBoolean()
    {
        var doc = ScenarioParser.Parse(
            """
            name: t
            steps:
              - expect_visible: { query: Initialized, exact: true }
              - expect_visible: { query: Initialized }
            """,
            fallbackName: "t");

        Assert.True(doc.Steps[0].GetBool("exact", false));
        Assert.False(doc.Steps[1].GetBool("exact", false));
    }

    [Fact]
    public void Parse_ClickUntilVisible_KeepsRetryProperties()
    {
        var doc = ScenarioParser.Parse(
            """
            name: t
            steps:
              - click:
                  query: Initialize
                  untilVisible: Initialized
                  untilExact: true
                  retryMs: 1500
            """,
            fallbackName: "t");

        Assert.Equal("Initialized", doc.Steps[0].Get("untilVisible"));
        Assert.True(doc.Steps[0].GetBool("untilExact", false));
        Assert.Equal(1500, doc.Steps[0].GetInt("retryMs", -1));
    }

    [Fact]
    public void Parse_MissingName_FallsBackToFileName()
    {
        var doc = ScenarioParser.Parse("steps:\n  - stop_all\n", fallbackName: "my-file");

        Assert.Equal("my-file", doc.Name);
        Assert.Equal(ScenarioVerbs.StopAll, doc.Steps[0].Verb);
    }

    [Fact]
    public void Parse_KeepOpenTrue_IsHonored()
    {
        var doc = ScenarioParser.Parse(
            "name: t\nkeepOpen: true\nsteps:\n  - stop_all\n", fallbackName: "t");

        Assert.True(doc.KeepOpen);
    }

    [Fact]
    public void Parse_ForegroundTrue_IsHonored()
    {
        var doc = ScenarioParser.Parse(
            "name: t\nforeground: true\nsteps:\n  - stop_all\n", fallbackName: "t");

        Assert.True(doc.Foreground);
    }

    [Fact]
    public void Parse_ForegroundOmitted_DefaultsToMinimized()
    {
        var doc = ScenarioParser.Parse("name: t\nsteps:\n  - stop_all\n", fallbackName: "t");

        Assert.False(doc.Foreground);
    }

    [Fact]
    public void Parse_StartStepFlags_AreReadableAsBooleans()
    {
        var doc = ScenarioParser.Parse(
            """
            name: t
            steps:
              - start_app: { path: App.exe, session: oi, foreground: true }
              - start_process: { path: Host.exe, session: sup, showWindow: false }
            """,
            fallbackName: "t");

        Assert.True(doc.Steps[0].GetBool("foreground", false));
        Assert.False(doc.Steps[1].GetBool("showWindow", true));
    }

    [Fact]
    public void Parse_ScalarShorthand_MapsToDefaultProperty()
    {
        var doc = ScenarioParser.Parse(
            "name: t\nsteps:\n  - sleep: 500\n  - click: Login\n", fallbackName: "t");

        Assert.Equal("500", doc.Steps[0].Get("ms"));
        Assert.Equal("Login", doc.Steps[1].Get("query"));
    }

    [Fact]
    public void Parse_BareVerb_NoProperties()
    {
        var doc = ScenarioParser.Parse("name: t\nsteps:\n  - stop_all\n", fallbackName: "t");

        Assert.Equal(ScenarioVerbs.StopAll, doc.Steps[0].Verb);
        Assert.Empty(doc.Steps[0].Props);
    }

    [Fact]
    public void Parse_VariableSubstitution_PrefersOverrideThenVarsThenEnv()
    {
        Environment.SetEnvironmentVariable("UIPILOT_TEST_VAR", "from-env");
        try
        {
            var doc = ScenarioParser.Parse(
                """
                name: t
                vars:
                  user: from-vars
                steps:
                  - type: { query: User, text: "${user}" }
                  - type: { query: Env, text: "${UIPILOT_TEST_VAR}" }
                """,
                fallbackName: "t",
                overrides: new Dictionary<string, string> { ["user"] = "from-override" });

            Assert.Equal("from-override", doc.Steps[0].Get("text"));
            Assert.Equal("from-env", doc.Steps[1].Get("text"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("UIPILOT_TEST_VAR", null);
        }
    }

    [Fact]
    public void Parse_UnresolvedVariable_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse(
                "name: t\nsteps:\n  - click: { query: \"${missing}\" }\n", fallbackName: "t"));

        Assert.Contains("missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownVerb_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse("name: t\nsteps:\n  - frobnicate: {}\n", fallbackName: "t"));

        Assert.Contains("frobnicate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingRequiredProperty_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse("name: t\nsteps:\n  - click: { session: oi }\n", fallbackName: "t"));

        Assert.Contains("Step 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RequiredGroup_AcceptsEitherAlternative()
    {
        var byId = ScenarioParser.Parse("name: t\nsteps:\n  - click: { id: e1 }\n", fallbackName: "t");
        Assert.Equal("e1", byId.Steps[0].Get("id"));

        var byQuery = ScenarioParser.Parse("name: t\nsteps:\n  - click: { query: Login }\n", fallbackName: "t");
        Assert.Equal("Login", byQuery.Steps[0].Get("query"));
    }

    [Fact]
    public void Parse_MultipleKeysInOneStep_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse(
                "name: t\nsteps:\n  - click: { query: A }\n    type: { query: B, text: c }\n",
                fallbackName: "t"));

        Assert.Contains("exactly one verb", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NoStepsKey_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() => ScenarioParser.Parse("name: t\n", fallbackName: "t"));
        Assert.Contains("steps", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_InvalidYaml_WrapsInScenarioException()
    {
        Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse("steps: [this is: not: valid", fallbackName: "t"));
    }

    [Fact]
    public void ParseFile_MissingFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "uipilot-tests", Guid.NewGuid().ToString("N") + ".yaml");
        Assert.Throws<ScenarioException>(() => ScenarioParser.ParseFile(missing));
    }

    [Fact]
    public void Parse_NestedPropertyValue_Throws()
    {
        var ex = Assert.Throws<ScenarioException>(() =>
            ScenarioParser.Parse(
                "name: t\nsteps:\n  - click:\n      query: Login\n      nested:\n        a: b\n",
                fallbackName: "t"));

        Assert.Contains("scalar", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
