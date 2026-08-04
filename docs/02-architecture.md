# Architecture

```mermaid
flowchart TB
  subgraph consumers [Target apps]
    WpfApp["WPF: WpfPilotHost.Start()"]
    AvaApp["Avalonia: AvaloniaPilotHost.Start()"]
  end
  subgraph core [WpfPilot.Core]
    Runtime[PilotRuntime]
    Pipe[NamedPipeServer JSON-RPC]
    Reg[ToolRegistry + BuiltInTools]
    Contract[IUiBackend]
    Disc[DiscoveryFile]
  end
  subgraph adapters [Framework adapters]
    WpfBack[WpfUiBackend]
    AvaBack[AvaloniaUiBackend]
  end
  subgraph agentSide [Agent side]
    CLI["WpfPilot.Cli (stdio MCP + launcher)"]
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
  CLI -->|JSON-RPC over named pipe| Pipe
  CLI -->|reads discovery files| Disc
```

## Shared core (`src/WpfPilot.Core`)

| Component | Role |
|---|---|
| `Hosting/PilotRuntime` | Start/stop, enablement gate, wiring pipe + discovery + tools. |
| `PilotOptions` / `UiFrameworks` | Shared options + `wpf` / `avalonia` labels. |
| `Abstraction/IUiBackend` | Framework-neutral automation contract used by built-in tools. |
| `Server/*` | Named pipe, JSON-RPC, discovery file, pipe integrity (Windows). |
| `Tools/*` | `ToolRegistry`, `ToolContext`, `BuiltInTools` (identical tool names for every backend). |
| `Inspection/ElementInfo`, `ElementRegistry` | Agent-facing DTOs + weak handles (`object`). |
| `Interaction/RealInput` | OS SendInput drag (Windows). |

### Threading

The pipe accept loop runs on a background thread. Every tool that touches UI objects is
marshaled onto the app UI thread via `ToolContext.OnUi(...)`, which each host supplies
(WPF `Dispatcher` or Avalonia `Dispatcher.UIThread`).

## WPF adapter (`src/WpfPilot`)

`WpfPilotHost` + `WpfUiBackend` wrap the existing WPF visual-tree, UIA, binding-trace,
adorner, and `RenderTargetBitmap` implementations. Public API stays `WpfPilotHost.Start()`.

## Avalonia adapter (`src/AvaloniaPilot`)

`AvaloniaPilotHost` + `AvaloniaUiBackend` implement the same `IUiBackend` surface using Avalonia
visual/logical trees, control events/commands, logging-sink binding capture, and Avalonia
`RenderTargetBitmap`.

## CLI (`src/WpfPilot.Cli`)

Unchanged role: MCP bridge + lifecycle. Discovery now surfaces `uiFramework`. Launch sets both
`UIPILOT_ENABLE` / `WPFPILOT_ENABLE` (and the start-minimized pair) so either host enables.
