# Roadmap

Priority order, following the "thin vertical slice first" principle.

## Phase 0-1 (MVP) - done

- Packages `UiPilot.Wpf` / `UiPilot.Avalonia` + `PilotHost.Start()` (clean break from former WpfPilot names).
- MCP-over-pipe (`.NET 8+` only) + per-run token auth + discovery file (`%TEMP%/uipilot`, `UIPILOT_*` env only).
- Built-in tools + `UiPilot.Cli` MCP bridge + lifecycle tools.
- `samples/SampleApp` / `AvaloniaSampleApp` end-to-end loop.

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
- **Multi-session CLI**: named sessions, `list_sessions` / `select_session`, optional `session` on
  forwarding tools, `start_app` (prebuilt exe), `start_process` + `wait_for_log` (generic readiness),
  `stop_all`, per-session restart/stop.

## Phase 4 - real input - partially done

- `drag` via SendInput (Windows) shipped.
- Broader real-input mode / FlaUI parity still optional.
- Non-Windows input backends for Avalonia (deferred).

## Phase 5 - multi-UI - done for Avalonia

- `UiPilot.Core` + `IUiBackend`.
- `UiPilot.Avalonia` + sample app.
- Discovery `uiFramework`; env `UIPILOT_ENABLE` / `UIPILOT_START_MINIMIZED` only (no legacy aliases).

## Phase 5.5 - scenario runner - done

- `ScenarioParser` (YAML + `${var}` substitution) and `ScenarioRunner` over `ConnectionManager`.
- CLI `run <file-or-folder>` (exit code) and `run_scenario` MCP tool.
- Fail-fast execution, failure screenshots, `report.json` artifacts.
- Sample scenarios for `AvaloniaSampleApp` and the ECF Atmospheric sample.

## Phase 6 - packaging / extensibility

- `[PilotTool]` discovery + auto-registration.
- `services.AddUiPilot()` / Avalonia host helpers.
- Publish `uipilot` CLI as a `dotnet tool`.
- Debug-only MSBuild props / module initializer auto-`Start()`.

## Deferred (tracked leftovers)

- `[PilotTool]` attribute auto-discovery (tools are registered manually today).
- Publish `uipilot` CLI as a `dotnet tool`.
- Real input injection on non-Windows (Avalonia / cross-platform).
- WinUI / MAUI adapters sharing `UiPilot.Core`.
- Multi-transport (REST/gRPC/TCP).
- ViewModel mutation tools.
- Full visual-tree dump.
