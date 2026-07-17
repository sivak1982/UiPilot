# WpfPilot

In-process automation for **any WPF app**, built for AI coding agents (Cursor, Claude, etc.).

WpfPilot runs *inside* your WPF process, so it can inspect the live visual tree, read data
bindings, capture per-window screenshots, and drive synthetic input, then exposes all of that
to an agent over MCP. It beats external UI Automation for binding/ViewModel/layout diagnostics
because it has direct access to the running objects.

## The one required line

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    WpfPilot.WpfPilotHost.Start(); // only enabled in Debug / via env flag
}
```

That's it. No DI, no Generic Host, no attributes, no TCP port.

## How it fits together

```text
Cursor/Claude  --stdio MCP-->  WpfPilot.Cli  --JSON-RPC over named pipe-->  your WPF app (WpfPilot)
                                    |
                                    +-- build / launch / restart your app (the AI edit loop)
```

- **`WpfPilot`** (NuGet, in your app): `WpfPilotHost.Start()` + named-pipe server + built-in tools.
- **`WpfPilot.Cli`** (separate tool): the MCP server your agent launches. It discovers running
  apps, bridges MCP tool calls to the app's pipe, and owns build/launch/restart.

## Security defaults

- Disabled unless `#if DEBUG`, env `WPFPILOT_ENABLE=1`, or an explicit `Start(force: true)`.
- Named pipe only. No TCP, no remote surface by default.
- Per-run auth token written to `%TEMP%\wpfpilot\<pid>.json`; every request must present it.

## Repo layout

| Path | What |
|---|---|
| `src/WpfPilot` | In-process library (`net472;net8.0-windows`). |
| `src/WpfPilot.Cli` | Out-of-process stdio MCP bridge + app launcher. |
| `samples/SampleApp` | Minimal WPF app used to validate the loop. |
| `docs/` | Design review, architecture, adoption, security, tools, protocol, roadmap. |

Start with [docs/01-overview.md](docs/01-overview.md).

## Build

```powershell
dotnet build WpfPilot.sln
```
