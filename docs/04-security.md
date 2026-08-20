# Security Model

UiPilot exposes click / type / command-invoke on a running app. That is powerful, so the
defaults are deliberately conservative: "any WPF app" must never mean "any WPF app ships a
remote-control backdoor."

## Defaults

| Setting | Default |
|---|---|
| Enabled | Debug build, `UIPILOT_ENABLE=1`, or `Start(force: true)` |
| App automation transport | Named pipe (local only) |
| Auth | Per-run random token in the discovery file |
| Status transport | Disabled unless `UIPILOT_STATUS_TOKEN` is configured |
| Remote control / TCP | Off (not implemented) |
| Tree access | Query-first, depth/limit bounded |
| Custom tools | Opt-in registration only |

## Enablement gate

`Start()` runs only when at least one of these is true (see
[PilotRuntime.IsEnabled](../src/UiPilot.Core/Hosting/PilotRuntime.cs)):

1. The entry assembly was built in Debug (detected via `DebuggableAttribute.IsJITOptimizerDisabled`).
2. Environment variable `UIPILOT_ENABLE=1`.
3. `Start(force: true)` (or options `Force = true`).

Otherwise `Start()` logs and returns without opening anything. A developer who ships a Release
build with `Start()` still in startup code does **not** expose an automation surface.

The CLI sets `UIPILOT_ENABLE=1` (and `UIPILOT_START_MINIMIZED=1`) on apps it launches via
`build_and_start`, so the loop works for WPF, Avalonia, and WinForms hosts regardless of build configuration.

## Transport and auth

- Only a **named pipe** is opened for app automation: `uipilot.<pid>.<guid>`. There is no
  remote-control TCP listener.
- On start, a random token (two GUIDs) is generated and written to the discovery file.
- After connect, the client must pass a one-line token gate before MCP begins; mismatches get an
  auth failure and close. See [PipeSessionAuth](../src/UiPilot.Core/Server/PipeSessionAuth.cs) /
  [McpPipeServer](../src/UiPilot.Core/Server/McpPipeServer.cs).
- The discovery file lives in `%TEMP%/uipilot\<pid>.json`, readable by the current user. Treat
  the token like any local dev secret; it is scoped to a single app run and removed on shutdown.
  Prefer a private `DiscoveryDirectory` on shared machines.
- Low-integrity pipe labeling lets a Medium-IL agent attach to an elevated app; the token still
  authenticates every request. See [PipeIntegrity.cs](../src/UiPilot.Core/Server/PipeIntegrity.cs).

### Read-only Cursor status service

The CLI can expose operation status to the bundled Cursor extension. This does not replace or
proxy the named-pipe app transport:

- It is disabled when `UIPILOT_STATUS_TOKEN` is absent.
- It binds strictly to `127.0.0.1` (default port `17831`) and cannot be configured for a remote
  interface.
- `/v1/status` and the `/v1/events` WebSocket require the installer-generated bearer token
  (`Authorization: Bearer …`). Query-string tokens are rejected. Only `/health` is
  unauthenticated and returns a minimal availability response.
- Telemetry is metadata-only: operation name/category/outcome/timing, session identity,
  framework, process, and window title. Tool arguments, typed text, pipe names, app discovery
  tokens, and screenshot data are never put into status events.
- The token is stored in the UiPilot MCP environment entry and matching Cursor extension setting.
  Treat Cursor's user settings as local developer credentials.

## Elevated (requireAdministrator) apps

Many enterprise WPF apps run elevated (High integrity), while an MCP agent launched by the editor
runs at Medium integrity. Windows' default "no-write-up" policy would block the agent from
connecting to the elevated app's pipe. To support this common case, the pipe is created
(`CreateNamedPipe` with an explicit security descriptor) with a **Low mandatory integrity label**
plus a DACL granting Authenticated Users / Administrators
([PipeIntegrity.cs](../src/UiPilot.Core/Server/PipeIntegrity.cs)). Lower-integrity clients can
then connect; the per-run token still authenticates every request. If the native path fails, the
library falls back to a default pipe (same-integrity clients only).

Note: a Medium-integrity agent still cannot *launch* or *kill* an elevated app, so
`build_and_start` / `restart_app` / `stop_app` do not apply to elevated targets - run the app
yourself and use `attach`.

## Threat notes / non-goals

- UiPilot is a **local developer tool**. There is no authentication beyond the local token and
  no remote transport. Do not enable it on machines where untrusted local users run code.
- The synthetic input path can trigger any command the UI can trigger. Keep it to Debug/test.
