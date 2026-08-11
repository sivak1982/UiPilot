# Adoption

## Zero-edit launch (recommended for agents)

`start_app` / `build_and_start` set process-scoped `DOTNET_STARTUP_HOOKS` to
`UiPilot.*.StartupHook.dll` (shipped under the CLI's `hooks/avalonia` or `hooks/wpf`).
The hook waits for `Application.Current`, then calls `PilotHost.Start(force: true)`.

- No project reference or `PilotHost.Start()` in the target app is required.
- Framework is auto-detected from assemblies beside the exe (`Avalonia.dll` → avalonia,
  `PresentationFramework.dll` → wpf), or pass `uiFramework`.
- Disable with `useStartupHook: false` on `start_app`, or env `UIPILOT_STARTUP_HOOK=0`.
- The hook clears `DOTNET_STARTUP_HOOKS` in-process so child processes do not inherit it.

## In-app opt-in (optional)

Still supported and **idempotent** with the hook (`PilotHost.Start` is a no-op when already running).

### Contract

1. Reference `UiPilot.Wpf` or `UiPilot.Avalonia` (Core comes transitively) **or** launch via CLI hooks.
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

`Start()` is idempotent and a no-op in Release unless `UIPILOT_ENABLE=1` or `Force=true`. See [04-security.md](04-security.md).

## MCP config (Cursor / Claude)

```json
{
  "mcpServers": {
    "uipilot": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/UiPilot/src/UiPilot.Cli/UiPilot.Cli.csproj"]
    }
  }
}
```

Typical loop: `build_and_start` or `start_app` → `wait_for_element` / `find_elements` → `click` /
`type_text` / `screenshot` → `restart_app` after edits.

### Driving two apps (e.g. Simulation + OI)

1. Launch each UI with `start_app` (hooks inject UiPilot) **or** call `PilotHost.Start()` in each process.
2. `start_app(..., session: "sim")` and `start_app(..., session: "oi")` (or `attach` with session names).
3. For a non-UI host: `start_process(..., session: "host")` then `wait_for_log(pathOrGlob, pattern)` —
   path and regex come from the agent/project rules, not from UiPilot.
4. Pass `session` on forwarding tools, or `select_session` for a sticky default.
5. Use `list_sessions` to confirm; `stop_app(session)` / `stop_all` to tear down.

Element ids are per process — always pair an id with the session that produced it.

See [05-tools.md](05-tools.md).
