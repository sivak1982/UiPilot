# Security Model

WpfPilot exposes click / type / command-invoke on a running app. That is powerful, so the
defaults are deliberately conservative: "any WPF app" must never mean "any WPF app ships a
remote-control backdoor."

## Defaults

| Setting | Default |
|---|---|
| Enabled | Debug build, `UIPILOT_ENABLE=1` / `WPFPILOT_ENABLE=1`, or `Start(force: true)` |
| Transport | Named pipe (local only) |
| Auth | Per-run random token in the discovery file |
| Remote / TCP | Off (not implemented) |
| Tree access | Query-first, depth/limit bounded |
| Custom tools | Opt-in registration only |

## Enablement gate

`Start()` runs only when at least one of these is true (see
[PilotRuntime.IsEnabled](../src/WpfPilot.Core/Hosting/PilotRuntime.cs)):

1. The entry assembly was built in Debug (detected via `DebuggableAttribute.IsJITOptimizerDisabled`).
2. Environment variable `UIPILOT_ENABLE=1` or legacy `WPFPILOT_ENABLE=1`.
3. `Start(force: true)` (or options `Force = true`).

Otherwise `Start()` logs and returns without opening anything. A developer who ships a Release
build with `Start()` still in startup code does **not** expose an automation surface.

The CLI sets both `UIPILOT_ENABLE=1` and `WPFPILOT_ENABLE=1` on apps it launches via
`build_and_start`, so the loop works for WPF and Avalonia hosts regardless of build configuration.

## Transport and auth

- Only a **named pipe** is opened: `wpfpilot.<pid>.<guid>`. There is no network listener.
- On start, a random token (two GUIDs) is generated and written to the discovery file.
- **Every** pipe request must present the matching token; the server rejects mismatches with an
  `Unauthorized` error. See [Server/NamedPipeServer.Process](../src/WpfPilot.Core/Server/NamedPipeServer.cs).
- The discovery file lives in `%TEMP%\wpfpilot\<pid>.json`, readable by the current user. Treat
  the token like any local dev secret; it is scoped to a single app run and removed on shutdown.

## Elevated (requireAdministrator) apps

Many enterprise WPF apps run elevated (High integrity), while an MCP agent launched by the editor
runs at Medium integrity. Windows' default "no-write-up" policy would block the agent from
connecting to the elevated app's pipe. To support this common case, the pipe is created
(`CreateNamedPipe` with an explicit security descriptor) with a **Low mandatory integrity label**
plus a DACL granting Authenticated Users / Administrators
([Server/PipeIntegrity.cs](../src/WpfPilot/Server/PipeIntegrity.cs)). Lower-integrity clients can
then connect; the per-run token still authenticates every request. If the native path fails, the
library falls back to a default pipe (same-integrity clients only).

Note: a Medium-integrity agent still cannot *launch* or *kill* an elevated app, so
`build_and_start` / `restart_app` / `stop_app` do not apply to elevated targets - run the app
yourself and use `attach`.

## Threat notes / non-goals

- WpfPilot is a **local developer tool**. There is no authentication beyond the local token and
  no remote transport. Do not enable it on machines where untrusted local users run code.
- The synthetic input path can trigger any command the UI can trigger. Keep it to Debug/test.
