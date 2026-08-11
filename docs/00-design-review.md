# Design Review (Revised)

This is the reconciled version of the original ChatGPT UiPilot design review. It records the
critique that was accepted and the concrete decisions that shaped the MVP in this repo.

## Verdict

In-process automation (visual tree, bindings, screenshots) is the right core. The original
platform-first design drifted from the "few lines to adopt" goal and under-specified process
lifecycle and security. The MVP fixes those first and defers multi-transport / multi-UI theater.

## Flaws accepted and how they are resolved here

| # | Flaw | Resolution in this repo |
|---|------|-------------------------|
| 1 | "Few lines" vs opt-in `[Inspectable]` attributes contradict each other | Default open for Debug/automation builds; **no attributes** required for basic automation. Attributes (`[PilotTool]`) are reserved for opt-in custom domain tools only. See [PilotToolAttribute.cs](../src/UiPilot.Core/PilotToolAttribute.cs). |
| 2 | Process lifecycle (`launch_app`/`restart_app` from inside the app) is backwards | Split: in-process [UiPilot](../src/UiPilot.Wpf) inspects/interacts; out-of-process [UiPilot.Cli](../src/UiPilot.Cli) owns build/launch/restart/discover. The app never launches itself. |
| 3 | Fixed TCP port 7777 is a security + collision hazard | **Named pipe only.** No TCP. Unique pipe name per process. Per-run auth token in the discovery file. See [04-security.md](04-security.md). |
| 4 | Premature platform abstraction | v1 shipped WPF-only. Multi-UI is now introduced deliberately via a shared core (`UiPilot.Core` + `IUiBackend`) with thin WPF/Avalonia adapters — not REST/gRPC theater. |
| 5 | "Any WPF app" underspecified | Library targets `net8.0-windows` / `net8.0`; static `PilotHost.Start()` needs no DI or Generic Host. |
| 6 | Interaction fidelity oversold | v1 is explicitly "synthetic" (UI Automation invoke + RaiseEvent fallback), clearly labeled in results. Real-input/FlaUI mode deferred. |
| 7 | Full tree dumps break agents | Query-first API: `find_elements`/`inspect_element` with limits and depth. No full-tree dump tool. |
| 8 | ViewModel reflection is not generic | No ViewModel mutation tools in v1. Core surface = windows + tree + properties + screenshots + binding errors + synthetic input. |
| 9 | Package sprawl | Consumers see **one** package `UiPilot.Wpf`. The CLI is a separate developer tool. |
| 10 | Spec-first inverts risk | This thin vertical slice ships first; docs describe working code. |

## What changed from the original review's own recommendations

- **Enablement is stricter than "explicit `Start()` always on".** Calling `Start()` in a Release
  build is a no-op unless `Force=true` or `UIPILOT_ENABLE=1`. This is the safest reading of
  "any WPF app must not ship a remote-control backdoor" - a developer who forgets to remove
  `Start()` does not accidentally expose their shipped app. See
  [PilotHost.Start](../src/UiPilot.Wpf/PilotHost.cs).
- **The pipe speaks MCP** (`StreamServerTransport` in-app). `net472` was dropped so the app can
  host the MCP C# SDK. The CLI remains the agent stdio MCP server and an MCP client to the app
  (plus build/launch lifecycle). See [06-protocol.md](06-protocol.md).

See [09-design-diagram.md](09-design-diagram.md) for a diagram overview, and
[02-architecture.md](02-architecture.md) for the full picture.
