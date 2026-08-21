# UiPilot.Client

Write deterministic C# desktop UI tests after proving each interaction against the live app with
UiPilot's MCP tools. The agent explores the UI; the saved test runs later with `dotnet test` and
does not use a model.

## Add a test

Reference `UiPilot.Client` from a test project, never from the application project. If UiPilot was
installed locally, its installer has already registered the package source and the Cursor test
skill.

For a NuGet-only setup, install the packaged skill into the current repository once:

```powershell
dotnet msbuild Your.Tests.csproj -t:UiPilotInstallCursorSkill
```

Then ask the agent for the test in plain language. The agent must:

1. Start or attach to the application with UiPilot MCP.
2. Prove one test step at a time against the live UI.
3. Record the successful query, `exact` setting, element type, id source, and interaction method.
4. Write the C# test only after every step has been observed.
5. Run `dotnet test`.

Do not guess selectors or decompile this package. IntelliSense and the packaged XML documentation
describe the typed API.

## Example

```csharp
using UiPilot.Client;
using Xunit;

public sealed class GreetTests
{
    [Fact]
    public async Task Greets_by_name()
    {
        await using var pilot = new UiPilotClient();
        await pilot.StartAppAsync(sampleExe, session: "sample");

        var nameBox = (await pilot.WaitForElementAsync(
            "NameBox", exact: true, session: "sample")).Single();
        await pilot.TypeTextAsync(nameBox.Id, "UiPilot", session: "sample");

        var greet = (await pilot.WaitForElementAsync(
            "GreetButton", exact: true, session: "sample")).Single();
        await pilot.ClickAsync(greet.Id, session: "sample");

        var result = await pilot.WaitForElementAsync(
            "Hello, UiPilot!", exact: true, session: "sample");
        Assert.Contains(result.Elements,
            element => element.Visible && element.Text == "Hello, UiPilot!");
    }
}
```

## MCP to C#

| MCP tool | `UiPilotClient` method | Result |
|---|---|---|
| `start_app` | `StartAppAsync` | `SessionSnapshot` |
| `start_process` | `StartProcessAsync` | `SessionSnapshot` |
| `wait_for_log` | `WaitForLogAsync` | `LogWaitResult` |
| `find_elements` | `FindElementsAsync` | `ElementPageResult` |
| `wait_for_element` | `WaitForElementAsync` | `ElementPageResult` |
| `inspect_element` | `InspectElementAsync` | `ElementResult` |
| `find_ancestor` | `FindAncestorAsync` | `ElementResult` |
| `click` | `ClickAsync` | `InteractionResult` |
| `type_text` | `TypeTextAsync` | `InteractionResult` |
| `press_keys` | `PressKeysAsync` | `InteractionResult` |
| `select_item` | `SelectItemAsync` | `InteractionResult` |
| `screenshot` | `ScreenshotAsync` | `ScreenshotResult` |
| custom app tool | `InvokeAppToolAsync<T>` | product-owned `T` |

The client also exposes windows, drag, scroll, focus, commands, window state, binding errors,
layout analysis, highlighting, and multi-app sessions. All calls accept an optional session name;
element ids are valid only in the session that returned them.
