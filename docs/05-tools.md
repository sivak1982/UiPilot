# Tools (v1)

Two groups: **forwarding** tools run inside the app (require an attached app);
**lifecycle** tools run in the CLI (drive the edit loop). All are exposed to the agent over MCP.

## Lifecycle tools (CLI, out-of-process)

Defined in [Tools/LifecycleTools.cs](../src/WpfPilot.Cli/Tools/LifecycleTools.cs).

| Tool | Args | Description |
|---|---|---|
| `list_apps` | - | List running WpfPilot apps from the discovery directory. |
| `attach` | `pid?` | Attach to an app (auto-selects if exactly one is running). |
| `build_and_start` | `project`, `configuration="Debug"` | Build a WPF project, launch it with WpfPilot enabled, and attach. |
| `restart_app` | - | Rebuild + relaunch the last started app and re-attach. |
| `stop_app` | - | Stop the launched app and detach. |

## Forwarding tools (in-app)

Defined in [Tools/ForwardingTools.cs](../src/WpfPilot.Cli/Tools/ForwardingTools.cs), implemented
in [Tools/BuiltInTools.cs](../src/WpfPilot/Tools/BuiltInTools.cs).

| Tool | Args | Returns |
|---|---|---|
| `list_windows` | - | Windows with identity + bounds. |
| `find_elements` | `query?`, `limit=50`, `root?` | Element summaries (handle id, type, name, AutomationId, text, bounds, enabled, visible, childCount). |
| `inspect_element` | `id`, `includeChildren=false`, `depth=1` | One element, optionally with children. |
| `click` | `id` | `{ method }` - `synthetic:automation-invoke`, `synthetic:automation-toggle`, or `synthetic:raise-click`. |
| `type_text` | `id`, `text` | `{ method }`. |
| `invoke_command` | `id` | Executes the element's bound `ICommand`. |
| `screenshot` | `id?` | Saves a PNG to a temp file; returns `{ path, width, height }`. |
| `get_binding_errors` | `clear=false` | Captured WPF binding errors/warnings. |
| `analyze_layout` | `root?` | Zero-size and off-screen visible elements. |
| `highlight_element` | `id`, `durationMs=1500` | Briefly overlays the element. |

### Element handles

`find_elements` / `list_windows` return a stable `id` (e.g. `e42`) per element. Pass it to any
tool that takes `id`. Handles are weak references; if the element is collected or the tree
changes, the tool returns an "unknown or collected element" error and you should re-query.

### Interaction fidelity

Input is **synthetic** (UI Automation patterns with a `RaiseEvent` fallback). It does not go
through real hit-testing, mouse capture, or `Preview*` tunneling. A real-input (SendInput/FlaUI)
mode is on the roadmap; see [07-roadmap.md](07-roadmap.md).

## Custom domain tools

Basic automation never needs attributes. For app-specific actions, register a handler on
`WpfPilotHost.Tools` after `Start()`, or (post-MVP) annotate a static method with
`[WpfPilotTool]` ([Attributes/WpfPilotToolAttribute.cs](../src/WpfPilot/Attributes/WpfPilotToolAttribute.cs)).
