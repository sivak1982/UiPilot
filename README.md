# WpfPilot (+ AvaloniaPilot)

In-process automation for **WPF and Avalonia** desktop apps, built for AI coding agents
(Cursor, Claude, etc.).

The automation library runs *inside* your process, so it can inspect the live visual tree, read
data bindings, capture per-window screenshots, and drive synthetic input, then exposes all of
that to an agent over MCP. It beats external UI Automation for binding/ViewModel/layout
diagnostics because it has direct access to the running objects.

## The one required line

**WPF**

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    WpfPilot.WpfPilotHost.Start(); // only enabled in Debug / via env flag
}
```

**Avalonia**

```csharp
// App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    AvaloniaPilot.AvaloniaPilotHost.Start(); // same pipe/MCP protocol as WPF
    base.OnFrameworkInitializationCompleted();
}
```

That's it. No DI, no Generic Host, no attributes, no TCP port. Same agent tools either way.

## How it fits together

```text
Cursor/Claude  --stdio MCP-->  WpfPilot.Cli  --JSON-RPC over named pipe-->  your app (WpfPilot or AvaloniaPilot)
                                    |
                                    +-- build / launch / restart your app (the AI edit loop)
```

| Package | Role |
|---|---|
| **`WpfPilot.Core`** | Shared protocol, discovery, named pipe, tool registry, `IUiBackend` contract. |
| **`WpfPilot`** | WPF adapter + `WpfPilotHost.Start()` (`net472;net8.0-windows`). |
| **`AvaloniaPilot`** | Avalonia adapter + `AvaloniaPilotHost.Start()` (`net8.0`). |
| **`WpfPilot.Cli`** | Out-of-process stdio MCP bridge + app launcher (framework-agnostic). |

## Agent-facing highlights (protocol 1.1)

- `wait_for_element`, paged `find_elements` (`offset` / `hasMore`)
- Window control: `set_window_state`, `bring_to_front`, `detach`
- Input: `press_keys`, `scroll`, `focus`, `select_item`, real-mouse `drag`
- Screenshots returned as MCP **image content** (plus a temp path)
- Structured errors: `{ error, code, message, hint }`
- Custom tools: `describe_app_tools` / `invoke_app_tool`

Full catalog: [docs/05-tools.md](docs/05-tools.md).

## Security defaults

- Disabled unless `#if DEBUG`, env `UIPILOT_ENABLE=1` / `WPFPILOT_ENABLE=1`, or an explicit `Start(force: true)`.
- Named pipe only. No TCP, no remote surface by default.
- Per-run auth token written to `%TEMP%\wpfpilot\<pid>.json`; every request must present it.
- Discovery files include `uiFramework` (`wpf` or `avalonia`) so agents know which stack is attached.

## Repo layout

| Path | What |
|---|---|
| `src/WpfPilot.Core` | Shared core (protocol + backend contract). |
| `src/WpfPilot` | WPF in-process library. |
| `src/AvaloniaPilot` | Avalonia in-process library. |
| `src/WpfPilot.Cli` | Out-of-process stdio MCP bridge + app launcher. |
| `samples/SampleApp` | Minimal WPF app used to validate the loop. |
| `samples/AvaloniaSampleApp` | Minimal Avalonia app used to validate the loop. |
| `docs/` | Design review, architecture, adoption, security, tools, protocol, roadmap. |

Start with [docs/01-overview.md](docs/01-overview.md).

## Build

```powershell
dotnet build WpfPilot.sln
```

On non-Windows hosts, build the cross-platform projects (`WpfPilot.Core`, `AvaloniaPilot`, `WpfPilot.Cli`, samples/AvaloniaSampleApp, tests). The WPF projects require Windows Desktop targeting packs.
