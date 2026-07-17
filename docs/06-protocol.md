# Named-pipe Protocol

The in-app server and the CLI communicate over a Windows named pipe using a small,
JSON-RPC-flavored line protocol. This is intentionally *not* the MCP wire format - MCP lives only
in the CLI, which translates between MCP and this protocol.

## Transport

- Pipe name: `wpfpilot.<pid>.<guid>` (published in the discovery file).
- Encoding: UTF-8, no BOM.
- Framing: **one JSON object per line**, `\n`-terminated. No embedded newlines in a frame.
- One client at a time; requests are processed sequentially.

## Discovery file

Written to `%TEMP%\wpfpilot\<pid>.json` on start, deleted on clean shutdown. Schema
([Server/DiscoveryFile.cs](../src/WpfPilot/Server/DiscoveryFile.cs)):

```json
{
  "pid": 12345,
  "processName": "SampleApp",
  "pipeName": "wpfpilot.12345.0f1e2d...",
  "token": "3a1b...9c",
  "protocolVersion": "1.0",
  "startedUtc": "2026-07-17T07:00:00.0000000Z",
  "mainWindowTitle": "WpfPilot Sample"
}
```

The CLI validates that `pid` is still alive before using an entry, and deletes stale files.

## Request

```json
{ "jsonrpc": "2.0", "id": 1, "method": "find_elements", "token": "<token>", "params": { "query": "Greet", "limit": 20 } }
```

- `method`: `ping`, `describe`, or a tool name.
- `token`: required on every request; must match the discovery-file token.
- `params`: tool-specific object (may be omitted / empty).

## Response

Success:

```json
{ "jsonrpc": "2.0", "id": 1, "result": { "count": 1, "elements": [ /* ... */ ] } }
```

Error:

```json
{ "jsonrpc": "2.0", "id": 1, "error": { "code": -32001, "message": "Invalid or missing token." } }
```

### Error codes ([Server/JsonRpc.cs](../src/WpfPilot/Server/JsonRpc.cs))

| Code | Meaning |
|---|---|
| -32700 | Parse error (bad JSON). |
| -32600 | Invalid request. |
| -32601 | Method not found. |
| -32001 | Unauthorized (bad/missing token). |
| -32002 | Tool threw an exception (message included). |

## Control methods

- `ping` -> `{ "pong": true }`.
- `describe` -> `{ "tools": [ { "name", "description" }, ... ] }`.
