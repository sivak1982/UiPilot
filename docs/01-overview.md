# Overview

UiPilot lets an AI coding agent drive and inspect a running desktop UI app from the inside.
The same MCP tools and named-pipe protocol (**v1.2**) work for **WPF** (`UiPilot.Wpf`) and
**Avalonia** (`UiPilot.Avalonia`).

## Why in-process

External UI Automation sees the accessibility projection of your app. It cannot tell you *why*
a binding failed, what the DataContext is, or that an element rendered with zero size. The
in-process library has the live objects: visual tree, binding errors, layout, and
`RenderTargetBitmap` screenshots even when minimized/occluded.

## The pieces

| Package | Role |
|---|---|
| `UiPilot.Core` | Protocol, discovery, pipe, `IUiBackend`, `ToolCatalog`, `PilotRuntime` |
| `UiPilot.Wpf` | WPF adapter + `PilotHost.Start()` |
| `UiPilot.Avalonia` | Avalonia adapter + `UiPilot.Avalonia.PilotHost.Start()` |
| `UiPilot.Cli` | stdio MCP bridge + build/launch/restart loop |

## Agent edit loop

```mermaid
sequenceDiagram
  participant Agent
  participant Cli as UiPilot.Cli
  participant App as WPF or Avalonia app
  Agent->>Cli: build_and_start(project)
  Cli->>App: dotnet build + launch (UIPILOT_ENABLE=1)
  App->>App: Host.Start()
  App-->>Cli: %TEMP%/uipilot/pid.json (uiFramework)
  Cli->>App: named pipe + token
  Agent->>Cli: wait_for_element / click / screenshot / …
  Cli->>App: JSON-RPC
  Agent->>Cli: restart_app after edits
```

## Try it

- [03-adoption.md](03-adoption.md) — one-line wiring
- [05-tools.md](05-tools.md) — full MCP tool catalog
- [samples/SampleApp](../samples/SampleApp) / [samples/AvaloniaSampleApp](../samples/AvaloniaSampleApp)
