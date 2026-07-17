# Adoption

## Contract (non-negotiable)

1. Add the `WpfPilot` NuGet package.
2. Call `WpfPilotHost.Start()` once at startup.
3. Run the app; the agent connects via the discovery file / MCP.
4. No attributes required for basic UI automation.
5. No DI, no Generic Host, no TCP required.

## The one line

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    WpfPilot.WpfPilotHost.Start();
}
```

`Start()` is idempotent and safe to leave in shipped code: it is a no-op in Release builds unless
you explicitly force it. See [04-security.md](04-security.md).

## Modern host (optional)

If you use the Generic Host, calling `Start()` in `OnStartup` still works. A
`services.AddWpfPilot()` extension is on the roadmap; it is not required and does not change the
adoption contract.

## Frameworks

The library multi-targets `net472;net8.0-windows`, covering .NET Framework 4.7.2+ and modern
.NET WPF apps. `Start()` is a plain static method, so Prism/Caliburn/custom-shell apps that do
not use `Host.CreateDefaultBuilder` are supported without changes.

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

For a published tool you would point `command` at the built `wpfpilot.exe` instead.

Then, from the agent:

1. `build_and_start` with the path to your `.csproj` (or `attach` if the app is already running).
2. `find_elements`, `inspect_element`, `click`, `type_text`, `screenshot`, `get_binding_errors`, ...
3. `restart_app` after you edit code.

See [05-tools.md](05-tools.md) for the full tool list.
