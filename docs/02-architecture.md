# Architecture

```mermaid
flowchart TB
  subgraph consumers [Target apps]
    WpfApp["WPF: PilotHost.Start()"]
    AvaApp["Avalonia: UiPilot.Avalonia.PilotHost.Start()"]
  end
  subgraph core [UiPilot.Core]
    Runtime[PilotRuntime]
    Pipe[NamedPipeServer JSON-RPC line protocol]
    Reg[ToolRegistry + BuiltInTools + ToolCatalog]
    Contract[IUiBackend]
    Disc[DiscoveryFile]
  end
  subgraph adapters [Framework adapters]
    WpfBack[WpfUiBackend]
    AvaBack[AvaloniaUiBackend]
  end
  subgraph agentSide [Agent side]
    CLI["UiPilot.Cli MCP + launcher"]
    Cursor[Cursor / Claude]
  end
  WpfApp --> Runtime
  AvaApp --> Runtime
  Runtime --> Pipe
  Runtime --> Reg
  Reg --> Contract
  Contract --> WpfBack
  Contract --> AvaBack
  Runtime --> Disc
  Cursor -->|stdio MCP| CLI
  CLI -->|build / launch / restart| consumers
  CLI -->|JSON-RPC| Pipe
  CLI -->|discovery| Disc
```

## Core (`src/UiPilot.Core`)

| Piece | Role |
|---|---|
| `PilotRuntime` | Enablement, pipe, discovery, tool wiring; idempotent host start |
| `IUiBackend` / `FindPage` | Framework-neutral automation contract |
| `ToolCatalog` | Canonical built-in tool names (CLI + tests parity) |
| `PilotToolException` | Structured `error.data` codes for agents |
| `Server/*` | Named pipe, JSON-RPC, discovery, Low-IL pipe security |
| `BuiltInTools` | Identical tool surface for every adapter |

UI work marshals through `ToolContext.OnUi` (WPF `Dispatcher` / Avalonia `Dispatcher.UIThread`).
Real-input `drag` runs off the UI thread under its own lock.

## Adapters

- **WPF** — visual/logical tree, UIA peers, binding trace, adorners, DPI-aware screenshots.
- **Avalonia** — split under `Inspection/` + `Interaction/` + `Media/`; chained log sink for binding capture.

## CLI

- References Core (shared `DiscoveryInfo`).
- Forwards every `ToolCatalog` tool; extras: `describe_app_tools`, `invoke_app_tool`.
- Screenshot → MCP image content + path metadata.
- Lifecycle: attach filters, `detach`, structured error JSON.
