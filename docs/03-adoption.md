# Adoption

## Contract (non-negotiable)

1. Add the framework package (`WpfPilot` or `AvaloniaPilot`).
2. Call `Start()` once at startup.
3. Run the app; the agent connects via the discovery file / MCP.
4. No attributes required for basic UI automation.
5. No DI, no Generic Host, no TCP required.

## WPF — the one line

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    WpfPilot.WpfPilotHost.Start();
}
```

## Avalonia — the one line

```csharp
// App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    AvaloniaPilot.AvaloniaPilotHost.Start();
    base.OnFrameworkInitializationCompleted();
}
```

`Start()` is idempotent and safe to leave in shipped code: it is a no-op in Release builds unless
you explicitly force it (or set `UIPILOT_ENABLE=1` / `WPFPILOT_ENABLE=1`). See
[04-security.md](04-security.md).

## Frameworks

| Package | TFMs |
|---|---|
| `WpfPilot.Core` | `net472;net8.0` |
| `WpfPilot` | `net472;net8.0-windows` (+ `UseWPF`) |
| `AvaloniaPilot` | `net8.0` |
| `WpfPilot.Cli` | `net10.0` |

## Connecting an agent (Cursor/Claude)

Configure the CLI as an MCP server. Example MCP config entry:

```json
{
  "mcpServers": {
    "wpfpilot": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Sandbox/WpfPilot/src/WpfPilot.Cli/WpfPilot.Cli.csproj"]
    }
  }
}
```

Then, from the agent:

1. `build_and_start` with the path to your `.csproj` (WPF or Avalonia), or `attach` if already running.
2. `find_elements`, `inspect_element`, `click`, `type_text`, `screenshot`, `get_binding_errors`, ...
3. `restart_app` after you edit code.

`list_apps` / `attach` include `uiFramework` (`wpf` or `avalonia`).

See [05-tools.md](05-tools.md) for the full tool list.
