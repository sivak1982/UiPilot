# Tools

Two groups: **forwarding** tools run inside the app (require an attached app);
**lifecycle** tools run in the CLI (drive the edit loop). All are exposed to the agent over MCP.
Built-in in-app names are catalogued in [`ToolCatalog`](../src/UiPilot.Core/Tools/ToolCatalog.cs)
and registered by [`BuiltInTools`](../src/UiPilot.Core/Tools/BuiltInTools.cs).

Protocol version: **1.2**.

## Lifecycle tools (CLI)

Defined in [LifecycleTools.cs](../src/UiPilot.Cli/Tools/LifecycleTools.cs).

| Tool | Args | Description |
|---|---|---|
| `list_apps` | - | List running pilot apps from `%TEMP%/uipilot` (includes `uiFramework`). |
| `attach` | `pid?`, `processName?`, `uiFramework?` | Attach; filters apply when `pid` is omitted. |
| `detach` | - | Drop the pipe connection without killing the process. |
| `build_and_start` | `project`, `configuration="Debug"`, `platform?` | Build, launch with pilot enabled, attach. |
| `restart_app` | - | Rebuild + relaunch the last started app and re-attach. |
| `stop_app` | - | Kill the driven app and detach. |

## Forwarding tools (in-app)

Defined in [ForwardingTools.cs](../src/UiPilot.Cli/Tools/ForwardingTools.cs).

| Tool | Args | Returns / notes |
|---|---|---|
| `list_windows` | - | Windows with identity + bounds. |
| `find_elements` | `query?`, `limit=50`, `offset=0`, `root?` | `{ count, total, hasMore, offset, limit, elements }`; `count` is this page, `total` is all matches. |
| `inspect_element` | `id`, `includeChildren=false`, `depth=1`, `properties?` | One element; optional comma-separated property names. |
| `wait_for_element` | `query`, `root?`, `timeoutMs=10000`, `pollMs=200` | Polls until a match appears or times out. |
| `click` | `id` | `{ method }` synthetic click / toggle / expand. |
| `drag` | start: `id` **or** `fromX`/`fromY`; end: `toId` **or** `toX`/`toY` **or** `dx`/`dy`; optional `grabOffset*`, `steps`, `stepDelayMs`, `settleMs` | Real OS mouse drag (Windows SendInput). |
| `type_text` | `id`, `text` | `{ method }`. |
| `press_keys` | `keys`, `id?` | Combos (`Ctrl+S`) and specials (`Enter`, `Tab`, …). |
| `scroll` | `id`, `dx=0`, `dy=0` | Synthetic wheel scroll. |
| `focus` | `id` | Focus the element. |
| `select_item` | `id`, `text?`, `index?` | Select in lists/combos/tabs. |
| `invoke_command` | `id` | Execute bound `ICommand`. |
| `screenshot` | `id?` | MCP **image content** + `{ path, width, height }` text. |
| `set_window_state` | `id?`, `state`, `activate=false` | `minimized` \| `normal` \| `maximized`. |
| `bring_to_front` | `id?` | Restore + activate for human viewing. |
| `get_binding_errors` | `clear=false` | Captured binding warnings/errors. |
| `analyze_layout` | `root?` | `zero_size`, `off_screen`, `overlap`. |
| `highlight_element` | `id`, `durationMs=1500` | Brief red overlay. |
| `describe_app_tools` | - | Pipe `describe` — built-in + any custom `Tools.Register` handlers. |
| `invoke_app_tool` | `method`, `paramsJson?` | Generic forwarder for custom tools. |

### Element handles

`find_elements` / `list_windows` / `wait_for_element` return stable `id`s (e.g. `e42`).
Handles are weak; if collected, tools return structured `{ error, code: "stale_element", … }`.

### Interaction fidelity

- Default click/type/keys/scroll/select are **synthetic** (automation peers / routed events).
- `drag` uses **real OS mouse input** (Windows) so hit-testing and mouse capture run.
- Screenshots use `RenderTargetBitmap` and work while minimized.

### Structured errors

Failed tools return JSON like:

```json
{ "error": true, "code": "stale_element", "message": "...", "hint": "..." }
```

Common codes: `stale_element`, `not_found`, `ambiguous`, `not_attached`, `invalid_args`,
`unsupported`, `platform_unsupported`, `timeout`, `canceled`.

## Custom domain tools

Register on `PilotHost.Tools` / `UiPilot.Avalonia.PilotHost.Tools` after `Start()`, or annotate with
`[PilotTool]` (discovery wiring is still post-MVP; use
`describe_app_tools` + `invoke_app_tool` once registered manually).
