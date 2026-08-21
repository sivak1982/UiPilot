# Tools

Two groups: **forwarding** tools run inside the app (require an attached session);
**lifecycle** tools run in the CLI (drive the edit loop). All are exposed to the agent over MCP.
Built-in in-app names are catalogued in [`ToolCatalog`](../src/UiPilot.Core/Tools/ToolCatalog.cs)
and registered by [`BuiltInTools`](../src/UiPilot.Core/Tools/BuiltInTools.cs).

Protocol / discovery version: **2.0** (MCP over named pipe).

## Sessions (multi-app)

The CLI keeps **named sessions** so an agent can drive more than one pilot app at once
(e.g. a server UI and a client UI):

1. `start_app` / `build_and_start` / `attach` with `session: "sim"` and `session: "oi"`.
2. Call forwarding tools with `session: "oi"` **or** `select_session("oi")` then omit `session`.
3. Forwarding results include a `session` field so the agent always knows which app answered.
4. Element handles (`e42`) are **per session/process** — do not reuse an id from `sim` on `oi`.

When multiple sessions are attached and a tool omits `session` without an active selection,
the CLI returns `ambiguous` and hints to `list_sessions` / `select_session`.

## Lifecycle tools (CLI)

Defined in [LifecycleTools.cs](../src/UiPilot.Cli/Tools/LifecycleTools.cs).

| Tool | Args | Description |
|---|---|---|
| `list_apps` | - | List running pilot apps from `%TEMP%/uipilot` (includes `uiFramework`). |
| `list_sessions` | - | List attached sessions (`name`, `pid`, `isActive`, `canRestart`, …). |
| `select_session` | `session` | Sticky active session for forwarding tools that omit `session`. |
| `attach` | `pid?`, `processName?`, `uiFramework?`, `session?` | Attach without dropping other sessions. Session defaults to process name. |
| `detach` | `session?` | Drop one session's pipe without killing the process. |
| `build_and_start` | `project`, `configuration="Debug"`, `platform?`, `session?`, `foreground=false` | Build, launch with pilot enabled, attach. Replaces only the same session name. |
| `start_app` | `path`, `session?`, `workingDirectory?`, `foreground=false` | Launch a prebuilt `.exe`/`.dll` (no rebuild), attach. |
| `start_process` | `path`, `session?`, `workingDirectory?`, `arguments?`, `showWindow=true` | Launch a **non-pilot** process; track as `kind: process` (no MCP pipe). |
| `wait_for_log` | `pathOrGlob`, `pattern`, `timeoutMs=60000`, `pollMs=200`, `fromEnd=false` | Poll a file / newest glob match until regex matches (generic readiness). |
| `restart_app` | `session?` | Relaunch a CLI-started session (`build_and_start`, `start_app`, or `start_process`). |
| `stop_app` | `session?` | Kill one session's process tree and clear it. |
| `stop_all` | - | Kill every session's process tree. |

### Window visibility

UI apps start minimized so the agent/IDE stays visible, and screenshots work anyway (they render
offscreen). Pass `foreground: true` to `start_app` / `build_and_start` when a human is watching —
the app then starts visible and is pulled to the front. `bring_to_front` still works on demand.

Process sessions get their own console window by default (`showWindow: true`), so a console host
like a supervisor appears in the taskbar and its output stays out of the CLI's stdout stream. Pass
`showWindow: false` to have it share this console instead.

### Stopping spawned processes

`stop_app` / `stop_all` stop everything a session spawned, not just the process UiPilot launched.
Each launched process is placed in a Windows job object, so service hosts started by a supervisor
are terminated too — including ones that outlive their parent, which a plain process-tree kill
cannot reach.

### Readiness (console / non-UI)

Use `start_process` + `wait_for_log` for hosts that are not pilot UI apps. UiPilot stays
app-agnostic: the agent supplies the log path and regex.

```text
start_process(path: ".../SomeHost.exe", session: "host")
wait_for_log(pathOrGlob: ".../Logs/yyyyMMdd/*.log", pattern: "Startup completed")
start_app(path: ".../OperatorInterface.exe", session: "oi")
```

## Forwarding tools (in-app)

Defined in [ForwardingTools.cs](../src/UiPilot.Cli/Tools/ForwardingTools.cs).

Every forwarding tool accepts optional `session`. Object results are enriched with `session`.

| Tool | Args | Returns / notes |
|---|---|---|
| `list_windows` | `session?` | Windows with identity + bounds. |
| `find_elements` | `query?`, `limit=50`, `offset=0`, `root?`, `exact=false`, `session?` | `{ count, total, hasMore, offset, limit, elements, session }`; `count` is this page, `total` is all matches. |
| `inspect_element` | `id`, `includeChildren=false`, `depth=1`, `properties?`, `session?` | One element; optional comma-separated property names. |
| `find_ancestor` | `id`, `type?`, `maxDepth=25`, `session?` | Nearest ancestor of `type`. Use when a search matched a templated control's label (a nested TextBlock) but the ancestor carries the enabled state and the click. |
| `wait_for_element` | `query`, `root?`, `timeoutMs=10000`, `pollMs=200`, `exact=false`, `session?` | Polls until a match appears or times out. |
| `click` | `id`, `session?` | `{ method, session }` synthetic click / toggle / expand. |
| `drag` | start: `id` **or** `fromX`/`fromY`; end: `toId` **or** `toX`/`toY` **or** `dx`/`dy`; optional `grabOffset*`, `steps`, `stepDelayMs`, `settleMs`, `session?` | Real OS mouse drag (Windows SendInput). |
| `type_text` | `id`, `text`, `session?` | `{ method, session }`. |
| `press_keys` | `keys`, `id?`, `session?` | Combos (`Ctrl+S`) and specials (`Enter`, `Tab`, …). |
| `scroll` | `id`, `dx=0`, `dy=0`, `session?` | Synthetic wheel scroll. |
| `focus` | `id`, `session?` | Focus the element. |
| `select_item` | `id`, `text?`, `index?`, `session?` | Select in lists/combos/tabs. |
| `invoke_command` | `id`, `session?` | Execute bound `ICommand` (unsupported by WinForms). |
| `screenshot` | `id?`, `session?` | MCP **image content** + `{ path, width, height, session }` text. |
| `set_window_state` | `id?`, `state`, `activate=false`, `session?` | `minimized` \| `normal` \| `maximized`. |
| `resize_window` | `width`, `height`, `id?`, `x?`, `y?`, `activate=false`, `session?` | Restores to normal if needed, sets size (optional position); returns `{ x, y, width, height, state, session }`. |
| `bring_to_front` | `id?`, `session?` | Restore + activate for human viewing. |
| `get_binding_errors` | `clear=false`, `session?` | Captured binding warnings/errors; WinForms currently returns an empty list. |
| `analyze_layout` | `root?`, `session?` | `zero_size`, `off_screen`, `overlap`. |
| `highlight_element` | `id`, `durationMs=1500`, `session?` | Brief red overlay. |
| `describe_app_tools` | `session?` | Pipe `describe` — built-in + any custom `Tools.Register` handlers. |
| `invoke_app_tool` | `method`, `paramsJson?`, `session?` | Generic forwarder for custom tools. |

### Element handles

`find_elements` / `list_windows` / `wait_for_element` return stable `id`s (e.g. `e42`).
Handles are weak and **scoped to one session**; if collected, tools return structured
`{ error, code: "stale_element", … }`.

### Matching

`query` matches a case-insensitive substring of an element's type, name, AutomationId, tooltip,
or text, and an exact AutomationId match always wins over incidental substring matches elsewhere
in the tree.

Pass `exact: true` when a substring would be ambiguous — most often for state labels that contain
one another. Asserting `Initialized` also matches a `Not Initialized` label (and an
`initializedLed` control); `exact: true` requires the whole value to be equal.

### Interaction fidelity

- Default click/type/keys/scroll/select are **synthetic** (automation peers / routed events).
- `drag` uses **real OS mouse input** (Windows) so hit-testing and mouse capture run.
- WPF/Avalonia screenshots use `RenderTargetBitmap`; WinForms uses `DrawToBitmap` with a
  `PrintWindow` fallback. Owner-drawn or hosted native content may not capture completely.

### Structured errors

Failed tools return JSON like:

```json
{ "error": true, "code": "stale_element", "message": "...", "hint": "..." }
```

Common codes: `stale_element`, `not_found`, `ambiguous`, `not_attached`, `invalid_args`,
`unsupported`, `platform_unsupported`, `timeout`, `canceled`.

## Custom domain tools

Register on the matching adapter's `PilotHost.Tools` after `Start()`, or annotate a public
static method with `[PilotTool("name")]`. `PilotHost.Start()` scans the entry assembly and
registers those methods automatically; they then appear in `describe_app_tools` /
`invoke_app_tool`. Supported signatures: `(ToolContext, JsonElement)`, `(JsonElement)`, or
parameterless. Built-in tool names are never overwritten.

## Dual-app example

```text
start_app(path: ".../ServerUi.exe", session: "server")
start_process(path: ".../Host.exe", session: "host")
wait_for_log(pathOrGlob: ".../logs/*/host.log", pattern: "ready")
start_app(path: ".../ClientUi.exe", session: "client")
find_elements(query: "Connect", session: "client")
click(id: "e12", session: "client")
screenshot(session: "server")
select_session("client")      # sticky; later tools may omit session
stop_all()
```

Paths and the readiness regex are supplied by the agent (or project rules) — UiPilot does not
hard-code any product-specific log format.
