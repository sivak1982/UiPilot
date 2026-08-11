# Overview

UiPilot lets an AI coding agent drive and inspect a running desktop UI app from the inside.
The same MCP tools and **MCP-over-pipe** protocol (**v2.0**) work for **WPF** (`UiPilot.Wpf`) and
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
| `UiPilot.Wpf` | WPF adapter + `PilotHost.Start()` (`net8.0-windows`) |
| `UiPilot.Avalonia` | Avalonia adapter + `UiPilot.Avalonia.PilotHost.Start()` |
| `UiPilot.*.StartupHook` | `DOTNET_STARTUP_HOOKS` injectors (CLI `hooks/`) |
| `UiPilot.Cli` | stdio MCP bridge + build/launch/restart loop |

## Agent edit loop

```text
Agent                  UiPilot.Cli                         App (WPF / Avalonia)
  |                         |                                      |
  |-- start_app / --------->|                                      |
  |   build_and_start       |-- launch + DOTNET_STARTUP_HOOKS ---->|
  |   (session: "oi")       |                      Host.Start() ---|
  |                         |<-- %TEMP%/uipilot/<pid>.json --------|
  |                         |-- auth + MCP over named pipe ------->|
  |-- click(session=oi) --->|-- tools/call (MCP) ----------------->|
  |-- select_session ------>|                                      |
```

Multiple named sessions can be attached at once (Simulation + Operator Interface). See
[05-tools.md](05-tools.md#sessions-multi-app).

## Try it

- [03-adoption.md](03-adoption.md) — one-line wiring
- [05-tools.md](05-tools.md) — full MCP tool catalog
- [samples/SampleApp](../samples/SampleApp) / [samples/AvaloniaSampleApp](../samples/AvaloniaSampleApp)
