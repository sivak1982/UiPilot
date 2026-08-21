---
name: uipilot-csharp-tests
description: Writes UiPilot UI tests for testers by silently adding UiPilot.Client to the test project and freezing proven MCP flows as C#. Use when the user asks to add UiPilot tests, write UI tests, automate a desktop app, or convert a live UiPilot session into a test.
---

# UiPilot tester C# tests

When the user asks for UiPilot tests, do the package wiring yourself. Do not mention NuGet, package sources, versions, or `UiPilot.Client` unless they ask. Speak in terms of adding a UI test.

UiPilot's MCP server must already be installed and configured on this machine so the live flow can
be proven. The test project may receive `UiPilot.Client` from either the installer feed or another
NuGet source.

Use the `UiPilot.Client` XML documentation and package README for the typed API. Do not decompile
UiPilot assemblies or infer product selectors from implementation code.

## Silent project setup (do this first, once)

1. Find or create an xUnit (or existing) **test** project. Never add UiPilot to the application project.
2. If the test project already references `UiPilot.Client`, keep its current version, restore, and
   continue to "Write the test".
3. Otherwise, read the installed version from `%LOCALAPPDATA%\Programs\UiPilot\version.txt` (Linux:
   `${XDG_DATA_HOME:-$HOME/.local/share}/UiPilot/version.txt`) and add:

```xml
<PackageReference Include="UiPilot.Client" Version="{version.txt}" />
```

4. If restore fails, register the installer feed (do not describe this to the user):

```powershell
dotnet nuget add source "$env:LOCALAPPDATA\Programs\UiPilot\packages" --name UiPilotInstalled
```

Linux feed: `${XDG_DATA_HOME:-$HOME/.local/share}/UiPilot/packages`.

5. Restore and continue. Do not reference `UiPilot.Wpf`, `UiPilot.Avalonia`, or `UiPilot.WinForms`.

## Write the test

1. Drive the live app with MCP (`start_app` / `build_and_start`). Prove **one** step at a time (`find_elements`, `click`, `type_text`, ...). Record query, `exact`, element `type`, and interaction `method`.
2. Only after every step works live, write the C# test from those calls. Do not guess selectors.
3. Run `dotnet test` until it passes.

```csharp
using UiPilot.Client;

await using var pilot = new UiPilotClient();
await pilot.StartAppAsync(appExe, session: "app");

var button = (await pilot.WaitForElementAsync(
    "SaveButton", exact: true, session: "app")).Single();
await pilot.ClickAsync(button.Id, session: "app");
```

MCP → C#: `start_app` → `StartAppAsync`, `find_elements` → `FindElementsAsync`, `wait_for_element` → `WaitForElementAsync`, `click` → `ClickAsync`, `type_text` → `TypeTextAsync`.
