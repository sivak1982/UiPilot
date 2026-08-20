# UiPilot

In-process automation for **WPF, Avalonia, and WinForms** desktop apps, built for AI coding agents
(Cursor, Claude, etc.).

> **Repository:** [github.com/sivak1982/UiPilot](https://github.com/sivak1982/UiPilot).
> Earlier local/GitHub naming used `WpfPilot`; the product, packages, and solution are **UiPilot**.

The automation library runs *inside* your process, so it can inspect the live visual tree, read
data bindings, capture per-window screenshots, and drive synthetic input, then exposes all of
that to an agent over MCP. It beats external UI Automation for binding/ViewModel/layout
diagnostics because it has direct access to the running objects.

## Launch without editing the target app

`UiPilot.Cli` injects UiPilot via process-scoped **`DOTNET_STARTUP_HOOKS`** when you use
`start_app` / `build_and_start`. No project reference or `PilotHost.Start()` in the app is
required. One generic hook observes the live process and loads the first ready WPF, Avalonia, or
WinForms adapter. See [docs/03-adoption.md](docs/03-adoption.md).

Optional in-app opt-in (idempotent with the hook):

## The one optional line

**WPF**

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    UiPilot.Wpf.PilotHost.Start(); // only enabled in Debug / via env flag
}
```

**Avalonia**

```csharp
// App.axaml.cs
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    UiPilot.Avalonia.PilotHost.Start(); // same pipe/MCP protocol as WPF
    base.OnFrameworkInitializationCompleted();
}
```

**WinForms**

```csharp
// Program.cs, after ApplicationConfiguration.Initialize()
UiPilot.WinForms.PilotHost.Start();
Application.Run(new MainForm());
```

That's it. No DI, no Generic Host, no attributes, no TCP port. Same agent tools either way.

## How it fits together

```text
Cursor/Claude  --stdio MCP-->  UiPilot.Cli  --MCP over named pipe-->  your app (WPF, Avalonia, or WinForms adapter)
                                    |
                                    +-- build / launch / restart your app (the AI edit loop)
                                    +-- authenticated localhost status --> Cursor sidebar
```

| Package | Role |
|---|---|
| **`UiPilot.Core`** | Shared protocol, discovery, named pipe, tool registry, `IUiBackend` contract. |
| **`UiPilot.Wpf`** | WPF adapter + `PilotHost.Start()` (`net8.0-windows`). |
| **`UiPilot.Avalonia`** | Avalonia adapter + `PilotHost.Start()` (`net8.0`). |
| **`UiPilot.WinForms`** | WinForms adapter + `PilotHost.Start()` (`net8.0-windows`). |
| **`UiPilot.StartupHook`** | Generic runtime detector and `DOTNET_STARTUP_HOOKS` injector. |
| **`UiPilot.Cli`** | Out-of-process stdio MCP bridge + app launcher (framework-agnostic). |

## Agent-facing highlights (protocol 2.0)

- `wait_for_element`, paged `find_elements` (`offset` / `hasMore`)
- Window control: `set_window_state`, `resize_window`, `bring_to_front`, `detach`
- Input: `press_keys`, `scroll`, `focus`, `select_item`, real-mouse `drag`
- **Multi-session**: `list_sessions` / `select_session`, optional `session` on every forwarding tool,
  `start_app` for prebuilt exes, `start_process` + `wait_for_log` for non-UI readiness, `stop_all`
- Screenshots returned as MCP **image content** (plus a temp path)
- Structured errors: `{ error, code, message, hint }`
- Custom tools: `describe_app_tools` / `invoke_app_tool`
- Optional read-only Cursor sidebar and status-bar indicator for sessions and current/recent
  operations

Full catalog: [docs/05-tools.md](docs/05-tools.md).

## Durable C# regression tests

Use MCP interactively to prove a UI flow, then save the same commands as a normal C# test through
`UiPilot.Client`. Each call returns a typed response that the test can inspect and assert on.
Regression runs use `dotnet test` with no agent in the execution path.

See [docs/08-csharp-tests.md](docs/08-csharp-tests.md).

## Security defaults

- Disabled unless `#if DEBUG`, env `UIPILOT_ENABLE=1`, or an explicit `Start(force: true)`.
- App automation uses a named pipe only; there is no remote-control TCP surface.
- Per-run auth token written to `%TEMP%/uipilot/<pid>.json`; every request must present it.
- Discovery files include `uiFramework` (`wpf`, `avalonia`, or `winforms`) so agents know which stack is attached.
- The optional read-only status API is disabled without `UIPILOT_STATUS_TOKEN`, binds strictly to
  `127.0.0.1`, and never includes tool arguments, typed text, pipe names, tokens, or screenshots.

## Repo layout

| Path | What |
|---|---|
| `src/UiPilot.Core` | Shared core (protocol + backend contract). |
| `src/UiPilot.Wpf` | WPF in-process library. |
| `src/UiPilot.Avalonia` | Avalonia in-process library. |
| `src/UiPilot.WinForms` | WinForms in-process library. |
| `src/UiPilot.StartupHook` | Generic startup hook with isolated adapter payloads. |
| `src/UiPilot.Client` | Typed C# client for deterministic product tests. |
| `src/UiPilot.Cli` | Out-of-process stdio MCP bridge + app launcher. |
| `extensions/uipilot-status` | Read-only Cursor sessions/operations sidebar and status indicator. |
| `samples/SampleApp` | Minimal WPF app used to validate the loop. |
| `samples/AvaloniaSampleApp` | Minimal Avalonia app used to validate the loop. |
| `samples/WinFormsSampleApp` | Minimal WinForms app used to validate controls, menus, and the startup hook. |
| `docs/` | Design review, design diagram, architecture, adoption, security, tools, protocol, roadmap, C# tests. |

Start with [docs/01-overview.md](docs/01-overview.md) or the [docs/09-design-diagram.md](docs/09-design-diagram.md).

## Build

```powershell
dotnet build UiPilot.sln
```

On non-Windows hosts, `EnableWindowsTargeting` is set so Windows desktop TFMs restore; WPF and WinForms runtime automation still requires Windows.

## Build the Windows installer

```powershell
.\installer\build-installer.ps1
```

The generated per-user installer bundle checks the required .NET runtime, installs the CLI,
generic startup hook and Cursor Status VSIX, then merges matching token/port settings into
Cursor's MCP and user configuration. See
[installer/README.md](installer/README.md) for installation, prerequisites, and uninstall details.
