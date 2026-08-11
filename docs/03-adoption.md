# Adoption

## Contract

1. Reference `UiPilot.Wpf` or `UiPilot.Avalonia` (Core comes transitively).
2. Call `Start()` once at startup.
3. Point your agent at `UiPilot.Cli` as an MCP server.
4. No attributes required for basic automation.
5. No DI / Generic Host / TCP required.

## WPF

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    UiPilot.Wpf.PilotHost.Start();
}
```

## Avalonia

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

1. Call `PilotHost.Start()` in **each** UI process.
2. `start_app(..., session: "sim")` and `start_app(..., session: "oi")` (or `attach` with session names).
3. For a non-UI host: `start_process(..., session: "host")` then `wait_for_log(pathOrGlob, pattern)` —
   path and regex come from the agent/project rules, not from UiPilot.
4. Pass `session` on forwarding tools, or `select_session` for a sticky default.
5. Use `list_sessions` to confirm; `stop_app(session)` / `stop_all` to tear down.

Element ids are per process — always pair an id with the session that produced it.

See [05-tools.md](05-tools.md).
