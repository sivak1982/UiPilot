# Roadmap

Priority order, following the "thin vertical slice first" principle.

## Phase 0-1 (this MVP) - done

- One package `WpfPilot` + `WpfPilotHost.Start()`.
- Named-pipe server + per-run token auth + discovery file.
- Built-in tools: `list_windows`, `find_elements`, `inspect_element`, `click`, `type_text`,
  `invoke_command`, `screenshot`, `get_binding_errors`, `analyze_layout`, `highlight_element`.
- `WpfPilot.Cli` stdio MCP bridge + lifecycle tools (`list_apps`, `attach`, `build_and_start`,
  `restart_app`, `stop_app`).
- `samples/SampleApp` to validate the loop end-to-end.

## Phase 2 - diagnostics polish

- Return screenshots as MCP image content (not just a file path).
- Overlap detection in `analyze_layout`.
- Richer element properties on demand (dependency property snapshot).

## Phase 3 - extensibility

- `[WpfPilotTool]` discovery + registration for opt-in custom domain tools.
- `services.AddWpfPilot()` convenience for Generic Host apps.

## Phase 4 - real input

- Optional SendInput / FlaUI "real input" mode alongside the synthetic default.

## Phase 5 - packaging

- Debug-only MSBuild props that auto-call `Start()` via a module initializer (zero-line adoption).
- Publish `wpfpilot` CLI as a `dotnet tool`.

## Deferred (not planned for now)

- Multi-transport (REST/gRPC/TCP).
- Multi-UI abstractions (WinUI/MAUI/Avalonia).
- ViewModel mutation tools.
- Full visual-tree dump.
