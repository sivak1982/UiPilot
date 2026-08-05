# Roadmap

Priority order, following the "thin vertical slice first" principle.

## Phase 0-1 (MVP) - done

- One package `WpfPilot` + `WpfPilotHost.Start()`.
- Named-pipe server + per-run token auth + discovery file.
- Built-in tools + `WpfPilot.Cli` MCP bridge + lifecycle tools.
- `samples/SampleApp` end-to-end loop.

## Phase 2 - diagnostics polish - largely done

- MCP screenshot as image content (path retained).
- Overlap detection in `analyze_layout`.
- Opt-in property snapshot on `inspect_element`.
- Structured tool errors (`error.data.code` / hints).

## Phase 3 - agent loop ergonomics - largely done

- `wait_for_element`, pagination (`offset` / `hasMore`).
- Window control over MCP (`set_window_state`, `bring_to_front`).
- `press_keys`, `scroll`, `focus`, `select_item`.
- `describe_app_tools` / `invoke_app_tool` for custom handlers.
- `detach` + attach filters (`processName`, `uiFramework`).

## Phase 4 - real input - partially done

- `drag` via SendInput (Windows) shipped.
- Broader real-input mode / FlaUI parity still optional.
- Non-Windows input backends for Avalonia (deferred).

## Phase 5 - multi-UI - done for Avalonia

- `WpfPilot.Core` + `IUiBackend`.
- `AvaloniaPilot` + sample app.
- Discovery `uiFramework`; `UIPILOT_*` env aliases.

## Phase 6 - packaging / extensibility

- `[PilotTool]` discovery + auto-registration.
- `services.AddWpfPilot()` / Avalonia host helpers.
- Publish `wpfpilot` CLI as a `dotnet tool`.
- Debug-only MSBuild props / module initializer auto-`Start()`.

## Deferred

- Multi-transport (REST/gRPC/TCP).
- WinUI / MAUI adapters.
- ViewModel mutation tools.
- Full visual-tree dump.
