# Adoption

## Zero-edit launch (recommended for agents)

`start_app` / `build_and_start` set process-scoped `DOTNET_STARTUP_HOOKS` to
`UiPilot.StartupHook.dll`. The generic hook observes the target process, waits for the first live
WPF main window, Avalonia main window, or WinForms form, then loads that adapter from its isolated
`hooks/{framework}` payload and calls `PilotHost.Start(force: true)`.

- No project reference or `PilotHost.Start()` in the target app is required.
- No assembly-folder guessing is used. Selection is based on the live UI inside the process.
- For mixed-framework applications, pass `uiFramework` to restrict detection to `wpf`,
  `avalonia`, or `winforms`; otherwise the first ready UI wins.
- Disable with `useStartupHook: false` on `start_app`, or env `UIPILOT_STARTUP_HOOK=0`.
- The hook clears `DOTNET_STARTUP_HOOKS` in-process so child processes do not inherit it.

## In-app opt-in (optional)

Still supported and **idempotent** with the hook (`PilotHost.Start` is a no-op when already running).

### Contract

1. Reference `UiPilot.Wpf`, `UiPilot.Avalonia`, or `UiPilot.WinForms` (Core comes transitively) **or** launch via CLI hooks.
2. Call `Start()` once at startup **or** rely on `DOTNET_STARTUP_HOOKS` from the CLI.
3. Point your agent at `UiPilot.Cli` as an MCP server.
4. No attributes required for basic automation.
5. No DI / Generic Host / TCP required.

### WPF

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    UiPilot.Wpf.PilotHost.Start();
}
```

### Avalonia

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    UiPilot.Avalonia.PilotHost.Start();
    base.OnFrameworkInitializationCompleted();
}
```

### WinForms

```csharp
[STAThread]
static void Main()
{
    ApplicationConfiguration.Initialize();
    UiPilot.WinForms.PilotHost.Start();
    Application.Run(new MainForm());
}
```

WinForms startup-hook injection supports modern .NET applications. Legacy .NET Framework is not supported.
`Control.Name` is the preferred stable selector; UiPilot also surfaces text and accessibility metadata.

`Start()` is idempotent and a no-op in Release unless `UIPILOT_ENABLE=1` or `Force=true`. See [04-security.md](04-security.md).

## MCP config (Cursor / Claude)

```json
{
  "mcpServers": {
    "uipilot": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/UiPilot/src/UiPilot.Cli/UiPilot.Cli.csproj"],
      "env": {
        "UIPILOT_STATUS_PORT": "17831",
        "UIPILOT_STATUS_TOKEN": "use-a-random-local-secret"
      }
    }
  }
}
```

The Windows installer generates and preserves a random status token, writes the matching
`uipilotStatus.*` Cursor settings, and installs the bundled UiPilot Status VSIX when the Cursor
CLI is available. Without `UIPILOT_STATUS_TOKEN`, the CLI exposes no status listener. The
extension is read-only; it displays sessions and operations but cannot control an app.

Typical loop: `build_and_start` or `start_app` → `wait_for_element` / `find_elements` → `click` /
`type_text` / `screenshot` → `restart_app` after edits.

### Driving two apps

1. Launch each UI with `start_app` (hooks inject UiPilot) **or** call `PilotHost.Start()` in each process.
2. `start_app(..., session: "server")` and `start_app(..., session: "client")` (or `attach` with session names).
3. For a non-UI host: `start_process(..., session: "host")` then `wait_for_log(pathOrGlob, pattern)` —
   path and regex come from the agent/project rules, not from UiPilot.
4. Pass `session` on forwarding tools, or `select_session` for a sticky default.
5. Use `list_sessions` to confirm; `stop_app(session)` / `stop_all` to tear down.

Element ids are per process — always pair an id with the session that produced it.

See [05-tools.md](05-tools.md).

## Freezing an explored flow as a C# test

Reference `UiPilot.Client` from the product test project. Its methods mirror MCP tools and return
typed responses:

```csharp
await using var pilot = new UiPilotClient();
await pilot.StartAppAsync(appPath, session: "app");

var button = (await pilot.WaitForElementAsync(
    "SaveButton", exact: true, session: "app")).Single();
var clicked = await pilot.ClickAsync(button.Id, session: "app");

Assert.StartsWith("synthetic:", clicked.Method);
```

Explore first with MCP, then ask the agent to write the equivalent C# test. Product-specific
selectors and workflows stay in the product repository. See [08-csharp-tests.md](08-csharp-tests.md).
