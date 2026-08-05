# Adoption

## Contract

1. Reference `WpfPilot` or `AvaloniaPilot` (Core comes transitively).
2. Call `Start()` once at startup.
3. Point your agent at `WpfPilot.Cli` as an MCP server.
4. No attributes required for basic automation.
5. No DI / Generic Host / TCP required.

## WPF

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    WpfPilot.WpfPilotHost.Start();
}
```

## Avalonia

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    AvaloniaPilot.AvaloniaPilotHost.Start();
    base.OnFrameworkInitializationCompleted();
}
```

`Start()` is idempotent and a no-op in Release unless `UIPILOT_ENABLE=1` /
`WPFPILOT_ENABLE=1` or `Force=true`. See [04-security.md](04-security.md).

## MCP config (Cursor / Claude)

```json
{
  "mcpServers": {
    "wpfpilot": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/WpfPilot/src/WpfPilot.Cli/WpfPilot.Cli.csproj"]
    }
  }
}
```

Typical loop: `build_and_start` → `wait_for_element` / `find_elements` → `click` /
`type_text` / `screenshot` → `restart_app` after edits.

See [05-tools.md](05-tools.md).
