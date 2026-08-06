# Named-pipe Protocol

The in-app server and the CLI communicate over a Windows named pipe using a small,
JSON-RPC-flavored line protocol. This is intentionally *not* the MCP wire format - MCP lives only
in the CLI, which translates between MCP and this protocol.

**Protocol version: `1.2`** (discovery `uiFramework`, paged find, wait/press/scroll/focus/select,
window tools, structured `error.data`).

## Transport

- Pipe name: `uipilot.<pid>.<guid>` (published in the discovery file).
- Encoding: UTF-8, no BOM.
- Framing: **one JSON object per line**, `\n`-terminated. No embedded newlines in a frame.
- Up to **4 concurrent clients** (`PipeIntegrity.MaxInstances`); UI work is serialized by the
  app dispatcher. Long real-input `drag` uses its own input lock.

## Discovery file

Written to `%TEMP%/uipilot\<pid>.json` on start, deleted on clean shutdown
([DiscoveryFile.cs](../src/UiPilot.Core/Server/DiscoveryFile.cs)):

```json
{
  "pid": 12345,
  "processName": "SampleApp",
  "pipeName": "uipilot.12345.0f1e2d...",
  "token": "3a1b...9c",
  "protocolVersion": "1.2",
  "startedUtc": "2026-07-17T07:00:00.0000000Z",
  "mainWindowTitle": "UiPilot Sample",
  "uiFramework": "wpf"
}
```

`uiFramework` is `wpf` or `avalonia`. Treat the token as a local secret (same-user ACL on `%TEMP%`).

## Request

```json
{ "jsonrpc": "2.0", "id": 1, "method": "find_elements", "token": "<token>", "params": { "query": "Greet", "limit": 20, "offset": 0 } }
```

- `method`: `ping`, `describe`, or a tool name from [`ToolCatalog`](../src/UiPilot.Core/Tools/ToolCatalog.cs).
- `token`: required on every request.
- `params`: tool-specific object (may be omitted / empty).

## Response

Success:

```json
{ "jsonrpc": "2.0", "id": 1, "result": { "count": 1, "total": 1, "hasMore": false, "elements": [ /* ... */ ] } }
```

For paged tools, `count` is the number of elements in the returned page and `total` is the
number of matching elements before `limit`/`offset` paging.

Error:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32002,
    "message": "Unknown or collected element 'e9'.",
    "data": { "code": "stale_element", "hint": "Re-run find_elements / wait_for_element." }
  }
}
```

### Error codes ([JsonRpc.cs](../src/UiPilot.Core/Server/JsonRpc.cs))

| Code | Meaning |
|---|---|
| -32700 | Parse error (bad JSON). |
| -32600 | Invalid request. |
| -32601 | Method not found. |
| -32001 | Unauthorized (bad/missing token). |
| -32002 | Tool threw (message + optional `data.code` / `data.hint`). |

## Control methods

- `ping` -> `{ "pong": true }`.
- `describe` -> `{ "tools": [ { "name", "description" }, ... ] }`.
