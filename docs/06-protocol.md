# App ↔ CLI Protocol (MCP over named pipe)

The in-app host and `UiPilot.Cli` speak **MCP** on a Windows named pipe (stream transport).
Agents still talk to the CLI over **MCP stdio**; the CLI is an MCP client to the app.

**Discovery protocol version: `2.0`** (MCP-over-pipe; .NET 8+ only — `net472` is not supported).

```text
Cursor/Claude
   |  MCP / stdio
   v
UiPilot.Cli  --auth line-->  named pipe  --MCP stream-->  PilotHost (in-app)
   |  lifecycle (build/launch/attach)
   +--> discovery file (%TEMP%/uipilot/<pid>.json)
```

## Transport

- Pipe name: `uipilot.<pid>.<guid>` (published in the discovery file).
- Encoding: UTF-8, no BOM.
- Up to **4 concurrent clients** (`PipeIntegrity.MaxInstances`); UI work is serialized by the
  app dispatcher. Long real-input `drag` uses its own input lock.
- MCP framing: SDK `StreamServerTransport` / `StreamClientTransport` on the duplex pipe.

## Session auth (before MCP)

After connect, the client sends one JSON line and waits for `ok` before starting MCP:

```json
{"token":"<discovery-token>"}
```

```json
{"ok":true}
```

A bad token yields `{"ok":false,"error":"..."}` and the server closes the connection. Auth reads
exact bytes (no `StreamReader` buffering) so MCP frames on the same pipe stay intact.

## Discovery file

Written to `%TEMP%/uipilot/<pid>.json` on start, deleted on clean shutdown
([DiscoveryFile.cs](../src/UiPilot.Core/Server/DiscoveryFile.cs)):

```json
{
  "pid": 12345,
  "processName": "SampleApp",
  "pipeName": "uipilot.12345.0f1e2d...",
  "token": "3a1b...9c",
  "protocolVersion": "2.0",
  "startedUtc": "2026-07-17T07:00:00.0000000Z",
  "mainWindowTitle": "UiPilot Sample",
  "uiFramework": "wpf"
}
```

`uiFramework` is `wpf` or `avalonia`. Treat the token as a local secret (same-user ACL on `%TEMP%`).

## MCP surface (in-app)

After auth, the session is standard MCP:

| MCP method | Behavior |
|---|---|
| `tools/list` | Built-in + custom tools from `ToolRegistry` |
| `tools/call` | Invokes the named tool; args are a JSON object |
| `ping` | Liveness |

Tool success payloads are JSON text content blocks. Tool failures use `CallToolResult.IsError`
with JSON `{ "error": true, "code", "message", "hint?" }` (same codes as before:
`stale_element`, `not_found`, `invalid_args`, `timeout`, `canceled`, …).

Paged tools still return `count` (page size) and `total` (all matches) inside the JSON payload.

## CLI bridge

`UiPilot.Cli` exposes the same agent-facing MCP tools as before. Lifecycle tools
(`list_apps`, `attach`, `build_and_start`, …) stay in the CLI only. App tools are forwarded via
`McpPipeClient` (`tools/call`). `describe_app_tools` maps to in-app `tools/list`.
