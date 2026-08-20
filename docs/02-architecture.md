# Architecture

```text
Target apps                         UiPilot.Core
────────────                        ────────────
WPF: PilotHost.Start() ──┐          PilotRuntime
Avalonia: PilotHost.Start()┤          ├─ McpPipeServer (MCP over named pipe)
WinForms: PilotHost.Start()┤          ├─ ToolRegistry + BuiltInTools
  (or DOTNET_STARTUP_HOOKS)├────────► ├─ IUiBackend
                           │          └─ DiscoveryFile
                           │
Framework adapters                  Agent side
──────────────────                  ──────────
WpfUiBackend / AvaloniaUiBackend / WinFormsUiBackend
                                      Cursor/Claude
        ▲                             │ MCP stdio
        └──── IUiBackend ──────────── UiPilot.Cli
                                        ├─ MCP server (stdio) for agents
                                        ├─ McpPipeClient → app
                                        ├─ DOTNET_STARTUP_HOOKS (hooks/)
                                        └─ build / launch / restart
```

## Core (`src/UiPilot.Core`)

| Piece | Role |
|---|---|
| `PilotRuntime` | Enablement, MCP pipe host, discovery, tool wiring; idempotent host start |
| `IUiBackend` / `FindPage` | Framework-neutral automation contract |
| `ToolCatalog` | Canonical built-in tool names (CLI + tests parity) |
| `PilotToolException` | Structured tool error codes for agents |
| `Server/McpPipeServer` | Named pipe + session auth + MCP stream (`StreamServerTransport`) |
| `Server/PipeSessionAuth` | One-line token gate before MCP |
| `BuiltInTools` | Identical tool surface for every adapter |

Requires **.NET 8+** (MCP C# SDK in-process). UI work marshals through `ToolContext.OnUi`.
Real-input `drag` runs off the UI thread under its own lock.

## Adapters

- **WPF** (`net8.0-windows`) — visual/logical tree, UIA peers, binding trace, adorners, DPI-aware screenshots.
- **Avalonia** (`net8.0`) — split under `Inspection/` + `Interaction/` + `Media/`; chained log sink for binding capture.
- **WinForms** (`net8.0-windows`) — controls and ToolStrip trees, WinForms-native interactions, `DrawToBitmap`/`PrintWindow` screenshots.

## CLI

- References Core (shared `DiscoveryInfo`).
- Forwards every `ToolCatalog` tool over MCP-to-app; extras: `describe_app_tools`, `invoke_app_tool`.
- Screenshot → MCP image content + path metadata.
- Lifecycle: attach filters, `detach`, structured error JSON.
