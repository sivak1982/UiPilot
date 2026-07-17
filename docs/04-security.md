# Security Model

WpfPilot exposes click / type / command-invoke on a running app. That is powerful, so the
defaults are deliberately conservative: "any WPF app" must never mean "any WPF app ships a
remote-control backdoor."

## Defaults

| Setting | Default |
|---|---|
| Enabled | Debug build, `WPFPILOT_ENABLE=1`, or `Start(force: true)` |
| Transport | Named pipe (local only) |
| Auth | Per-run random token in the discovery file |
| Remote / TCP | Off (not implemented) |
| Tree access | Query-first, depth/limit bounded |
| Custom tools | Opt-in registration only |

## Enablement gate

`Start()` runs only when at least one of these is true (see
[WpfPilotHost.IsEnabled](../src/WpfPilot/WpfPilotHost.cs)):

1. The entry assembly was built in Debug (detected via `DebuggableAttribute.IsJITOptimizerDisabled`).
2. Environment variable `WPFPILOT_ENABLE=1`.
3. `Start(force: true)` (or `WpfPilotOptions.Force = true`).

Otherwise `Start()` logs and returns without opening anything. A developer who ships a Release
build with `Start()` still in `App.xaml.cs` does **not** expose an automation surface.

The CLI sets `WPFPILOT_ENABLE=1` on apps it launches via `build_and_start`, so the loop works
regardless of build configuration.

## Transport and auth

- Only a **named pipe** is opened: `wpfpilot.<pid>.<guid>`. There is no network listener.
- On start, a random token (two GUIDs) is generated and written to the discovery file.
- **Every** pipe request must present the matching token; the server rejects mismatches with an
  `Unauthorized` error. See [Server/NamedPipeServer.Process](../src/WpfPilot/Server/NamedPipeServer.cs).
- The discovery file lives in `%TEMP%\wpfpilot\<pid>.json`, readable by the current user. Treat
  the token like any local dev secret; it is scoped to a single app run and removed on shutdown.

## Threat notes / non-goals

- WpfPilot is a **local developer tool**. There is no authentication beyond the local token and
  no remote transport. Do not enable it on machines where untrusted local users run code.
- The synthetic input path can trigger any command the UI can trigger. Keep it to Debug/test.
