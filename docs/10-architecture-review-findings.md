# UiPilot - Design, Code and Architecture Review Findings

**Date:** 2026-08-20  
**Scope:** Full static review of Core, StartupHook, WPF/Avalonia/WinForms adapters, CLI, Client, tests, Cursor status extension, installers, and scripts.  
**Method:** Four independent full-file review passes; critical/high claims spot-verified against source.  
**Working tree note:** Remediation edits started after this review were reverted. Pre-existing extension WIP was left untouched.

---

## Summary

| Severity | Count |
|----------|------:|
| Critical | 2 |
| High | 22 |
| Medium | ~50 |
| Low | ~27 |
| **Total** | **~101** |

**Verdict:** The architecture bones are sound (`IUiBackend` is clean, weak element handles are right, pipe auth framing and status-service security are careful). Implementation has six systemic failure modes; fixing those addresses most individual findings.

### Systemic root causes

1. **Global serialization on both sides of the pipe** - in-app `_invokeGate` and CLI `SemaphoreSlim` held across long operations; advertised concurrency is cosmetic.
2. **Blocking boundaries with no deadline** - UI marshal, pipe auth, and post-connect MCP init can hang forever; cancellation may be undeliverable due to sync-over-async.
3. **Contracts by convention** - stale elements via English exception-message match; empty MCP schemas; silent arg coercion; error codes drift per adapter.
4. **Adapter drift** - ~1,000 copy-pasted lines already diverge (`scroll`, `press_keys`, coordinates/DPI, click paths).
5. **Loose ownership / trust** - attach re-use kills foreign processes; discovery trusts PID liveness; pipe DACL admits all Authenticated Users.
6. **Inverted test pyramid + tooling rot** - adapters have almost no unit tests; scripts speak retired protocol; Linux installer version parse is broken.

---

## Critical

### C1. WinForms `press_keys` types into the wrong application
- **Where:** `src/UiPilot.WinForms/Interaction/SyntheticInput.cs` (~77)
- **What:** `SendKeys.SendWait` delivers OS-global keystrokes to the desktop foreground window. UiPilot launches targets with `UIPILOT_START_MINIMIZED=1`, so this typically types into the IDE or another focused app.
- **Contrast:** WPF/Avalonia raise in-process events and work offscreen.
- **Fix direction:** Post `WM_KEYDOWN`/`WM_CHAR` to the control handle, or fail with typed `unsupported`.

### C2. Linux installer extracts `LangVersion` as product version
- **Where:** `installer/build-installer.sh` (~21)
- **What:** Python takes first XML tag ending with `"Version"` -> `<LangVersion>12</LangVersion>` before `<Version>0.1.0</Version>`. Bundle becomes `UiPilot-12.0-...`; `install.sh` aborts on 4-part version check.
- **Contrast:** PowerShell builder uses a correct XPath.
- **Fix direction:** Select tag exactly `Version` (match PowerShell).

---

## High - Core + StartupHook

### H-Core-1. Global invoke lock serializes all tool calls
- **Where:** `src/UiPilot.Core/Tools/ToolRegistry.cs` (~62)
- **What:** `_invokeGate` held for entire handler duration; `wait_for_element` / `drag` block all other clients despite `MaxInstances = 4`.

### H-Core-2. `OnUi` has no timeout or cancellation
- **Where:** `src/UiPilot.Core/Tools/ToolContext.cs` (~30)
- **What:** Hung UI thread wedges the call forever while holding the invoke lock -> permanent automation surface deadlock.

### H-Core-3. Cancellation is shared mutable state on `ToolContext`
- **Where:** `src/UiPilot.Core/Tools/ToolContext.cs` (~27)
- **What:** Public settable `CancellationToken` forces the global lock; root cause of H-Core-1.

### H-Core-4. Sync-over-async MCP dispatch
- **Where:** `src/UiPilot.Core/Server/McpPipeServer.cs` (~129, ~158)
- **What:** `RunAsync().GetAwaiter().GetResult()` and synchronous `CallToolAsync` body likely prevent delivery of cancel notifications during long tools.

### H-Core-5. Stale-element contract is a string match
- **Where:** `src/UiPilot.Core/Tools/BuiltInTools.cs` (~355)
- **What:** Core detects stale handles by matching `"Unknown or collected element"` in `ArgumentException.Message`. Adapter wording drift -> generic `tool_error`.

### H-Core-6. Empty MCP input schemas for every tool
- **Where:** `src/UiPilot.Core/Server/McpPipeServer.cs` (~20, ~151)
- **What:** `tools/list` ships `{"type":"object","properties":{}}`; real contracts live only as prose in descriptions.

### H-Core-7. Unauthenticated pipe DoS (no auth timeout / length cap)
- **Where:** `src/UiPilot.Core/Server/PipeSessionAuth.cs` (~112)
- **What:** Connect + silence holds one of 4 instances forever; newline-free stream grows memory unboundedly. Any local user can do this (see M-Core DACL).

### H-Core-8. `ToolRegistry` dictionary not thread-safe
- **Where:** `src/UiPilot.Core/Tools/ToolRegistry.cs` (~18-30)
- **What:** Public `Register` after `Start()` races pipe-thread `Contains`/`List`/`Invoke` on a plain `Dictionary` -> undefined behavior.

---

## High - Adapters

### H-Ad-1. Avalonia `GetElementCentre` mixes DPI units
- **Where:** `src/UiPilot.Avalonia/AvaloniaUiBackend.cs` (~102)
- **What:** `PointToScreen` (physical) + `Bounds` half-size (logical) -> drag misses at non-100% scaling.

### H-Ad-2. Three incompatible `scroll` semantics
- **Where:** WPF / Avalonia / WinForms `SyntheticInput` / `Input`
- **What:** WPF raw wheel delta (small values ~ no-op) and discards `dx`; Avalonia treats as scroll lines; WinForms raw delta with HWheel.

### H-Ad-3. WPF modifier shortcuts silently no-op
- **Where:** `src/UiPilot.Wpf/Interaction/SyntheticInput.cs` (~270)
- **What:** Synthetic `KeyEventArgs` never updates `Keyboard.Modifiers`; `Ctrl+S` looks like bare `S`; `InputBindings` never fire. Avalonia sets modifiers correctly.

### H-Ad-4. `ElementInfo` / layout mix physical and logical units
- **Where:** WPF `VisualTreeQuery` / `LayoutAnalyzer`; Avalonia equivalents
- **What:** X/Y physical, Width/Height DIP -> wrong rectangles and false layout issues at 125%/150% DPI.

### H-Ad-5. Large copy-paste duplication across adapters
- **Where:** All three adapters
- **What:** Key grammar, `FindPage` paging, property readers, diagnostics ring buffers (~1k lines) duplicated and already diverged.

### H-Ad-6. Avalonia `Click` weaker than WPF
- **Where:** `src/UiPilot.Avalonia/Interaction/Input.cs` (~57)
- **What:** No automation-peer path; bound `Command` executes without raising `Click`.

---

## High - CLI + Client

### H-Cli-1. Global `SemaphoreSlim` held across 30s launch
- **Where:** `src/UiPilot.Cli/ConnectionManager.cs` (~182)
- **What:** Gate held through discovery + connect; stalls other sessions, tools, and status polls.

### H-Cli-2. `SendAsync` uses `Client` outside lock
- **Where:** `src/UiPilot.Cli/ConnectionManager.cs` (~126)
- **What:** Concurrent stop/detach nulls/disposes client -> NRE/`ObjectDisposedException` escapes unstructured.

### H-Cli-3. Re-attach kills processes the CLI never launched
- **Where:** `src/UiPilot.Cli/ConnectionManager.cs` (~520)
- **What:** `else if (AttachedPid ... && Launched is null) KillByPid(oldPid)` - attach-only session rename/replace kills foreign PID.

### H-Cli-4. No timeout after pipe connect (auth + MCP init)
- **Where:** `src/UiPilot.Cli/Pipe/McpPipeClient.cs` (~33)
- **What:** `timeoutMs` covers only `ConnectAsync`; wedged app hangs CLI under global gate.

---

## High - Tests, Extension, Installer, Scripts

### H-Tool-1. `scripts/` speak retired pre-MCP protocol
- **Where:** `scripts/drive.ps1`, `pipe-call.ps1`, `smoke-test.ps1`
- **What:** Raw per-request token JSON-RPC; first line authenticates by accident, subsequent calls fail. Advertised smoke/debug tools are dead.

### H-Tool-2. E2E tests hardcode `bin\Debug` while installer runs Release
- **Where:** `tests/UiPilot.Tests/*ClientTests*.cs`, `WpfStartupHookTests.cs`; `installer/build-installer.ps1`
- **What:** Clean Release gate fails or silently tests stale Debug binaries.

### H-Tool-3. Parallel tests identify children via machine-wide `PING` scan
- **Where:** `tests/UiPilot.Tests/ConnectionManagerTests.cs` (~266)
- **What:** Sibling tests launch `ping`; `KillTree` can kill the wrong process -> flaky by design.

### H-Tool-4. Installers fail on JSONC Cursor settings
- **Where:** `installer/UiPilot.Installer.Common.ps1` (~100); Linux `UiPilot.Installer.Common.sh`
- **What:** Comments/trailing commas (legal in Cursor settings) hard-fail registration.

---

## Medium - Core

| ID | Finding | Location |
|----|---------|----------|
| M-Core-1 | `IUiBackend` is a 24-member God interface; no capability discovery | `IUiBackend.cs` |
| M-Core-2 | Discovery file keyed by PID only - hook + in-app Start collide | `DiscoveryFile.cs` |
| M-Core-3 | Pipe DACL grants Authenticated Users full access on every pipe | `PipeIntegrity.cs` |
| M-Core-4 | `Start()` no rollback - failed start can leak live pipe server | `PilotRuntime.cs` |
| M-Core-5 | Wrong-typed args silently fall back (GetInt/GetDouble); GetBool accepts strings | `Args.cs` |
| M-Core-6 | Screenshot / registry throw unstructured exceptions | `BuiltInTools.cs`, `ToolRegistry.cs` |
| M-Core-7 | StartupHook `Start(force: true)` bypasses documented enablement gate | `StartupHook.cs` |
| M-Core-8 | `Stop()` does not dispose CTS, join accept thread, or unblock auth-stuck workers | `McpPipeServer.cs` |
| M-Core-9 | `PilotRuntime.Stop` tears down backend under in-flight tool calls | `PilotRuntime.cs` |
| M-Core-10 | `drag` uncancellable; no upper clamps on steps/delays | `RealInput.cs` |
| M-Core-11 | `get_binding_errors` touches backend off UI thread; non-atomic clear | `BuiltInTools.cs` |

---

## Medium - Adapters

| ID | Finding | Location |
|----|---------|----------|
| M-Ad-1 | Avalonia screenshots leak `RenderTargetBitmap`; hardcoded 96 DPI | `Avalonia/Media/Shot.cs` |
| M-Ad-2 | WPF non-`FrameworkElement` screenshot falls back to main window | `Wpf/Media/Screenshot.cs` |
| M-Ad-3 | WinForms screenshot ClientSize vs full-window render clips content; minimized weak | `WinForms/Media/Screenshot.cs` |
| M-Ad-4 | WinForms PilotHost invoke can NRE; stale first form pinned | `WinForms/PilotHost.cs` |
| M-Ad-5 | All hosts Invoke UI while holding startup lock; unbounded marshal | PilotHosts |
| M-Ad-6 | Error codes inconsistent; WPF `Unsupported` helper unused | `WpfUiBackend.cs` |
| M-Ad-7 | Avalonia `FindAncestor` visual-only (cannot escape popups) | `Avalonia/Inspection/VisualTree.cs` |
| M-Ad-8 | WinForms `TypeText` sets `.Text` on any control | `WinForms/Interaction/SyntheticInput.cs` |
| M-Ad-9 | Avalonia scroll `PointerWheelEventArgs` with `null!` pointer | `Avalonia/Interaction/Input.cs` |
| M-Ad-10 | WinForms `get_binding_errors` silent empty (false green) | `WinFormsUiBackend.cs` |
| M-Ad-11 | Avalonia binding diagnostics `string.Format` on named templates loses values | `BindingDiagnostics.cs` |
| M-Ad-12 | Only WinForms retries early Start; minimize timing differs three ways | PilotHosts |

---

## Medium - CLI + Client

| ID | Finding | Location |
|----|---------|----------|
| M-Cli-1 | Screenshot forwarder hardcodes result schema; error envelope translated 3x | `ForwardingTools.cs` |
| M-Cli-2 | Missing startup hook DLL silently skipped -> misleading 30s timeout | `StartupHookLocator.cs` |
| M-Cli-3 | Discovery trusts PID liveness alone; `StartedUtc` unused | `DiscoveryReader.cs` |
| M-Cli-4 | Scattered hardcoded timeouts; fixed-interval polls | `ConnectionManager.cs` |
| M-Cli-5 | Exited sessions hidden from list but counted for ambiguity | `ConnectionManager.cs` |
| M-Cli-6 | `UiPilotClient` mixed sync/async; `ActiveSessionName` does disk I/O | `UiPilotClient.cs` |
| M-Cli-7 | Client packs hooks into source tree; CLI sources Compile-Included into Client public API | `UiPilot.Client.csproj` |
| M-Cli-8 | `WrapWithSession` throws on empty/`Undefined` tool results | `ConnectionManager.cs` |
| M-Cli-9 | Duplicated error mappers already diverged; `McpException` unmapped | Lifecycle/Forwarding tools |
| M-Cli-10 | `McpPipeClient.Dispose` sync-over-async under global gate | `McpPipeClient.cs` |
| M-Cli-11 | Job-object handles leak when apps exit naturally | `AppLauncher.cs` |
| M-Cli-12 | Cancelled `build_and_start` does not kill `dotnet build` | `AppLauncher.cs` |
| M-Cli-13 | `BuildAsync` takes last stdout line as TargetPath (breaks multi-TFM) | `AppLauncher.cs` |
| M-Cli-14 | Status poll rescans discovery dir 2x/s under global lock | `StatusService.cs` |

---

## Medium - Tests / Extension / Installer

| ID | Finding | Location |
|----|---------|----------|
| M-Tool-1 | Non-Windows E2E skips report Passed with zero assertions | Client/startup tests |
| M-Tool-2 | Integration tests mixed into one project with no traits | `UiPilot.Tests` |
| M-Tool-3 | `ReservePort` TOCTOU | `StatusTestSupport.cs` |
| M-Tool-4 | CLI tools / `OperationTelemetry` surface-only or untested | tests |
| M-Tool-5 | No multi-client / disconnect / Stop-during-session pipe tests | tests |
| M-Tool-6 | Extension retries forever on missing token (config error) | `extension/src/client.ts` |
| M-Tool-7 | HTTP recovery does not update captured snapshot -> sessions wipe | `client.ts` |
| M-Tool-8 | Manual `refresh()` flips healthy WS to error on transient HTTP fail | `client.ts` |
| M-Tool-9 | Extension: no reconnect/auth/dispose behavior tests | extension tests |
| M-Tool-10 | `@types/node` ^26 vs Node 20 runtime | `package.json` |
| M-Tool-11 | MSI RegisterCursor after InstallFinalize - no rollback | `Package.wxs` |
| M-Tool-12 | Stale `uipilot-*` MCP entries / token lookup | installer Common |
| M-Tool-13 | Uninstall leaves VSIX + status token (reconnect loop) | `Register-Cursor.ps1` |
| M-Tool-14 | `build-installer.ps1` Linux RIDs vs `powershell.exe`; zip drops +x | installer |
| M-Tool-15 | Port 17831 / SDK floor duplicated across 4+ places | status + installers |
| M-Tool-16 | `drive.ps1` ReadLine blocks forever; blind sleeps | scripts |

---

## Low (selected)

| Area | Finding |
|------|---------|
| Core | Token from `Guid.NewGuid()`; non-constant-time compare |
| Core | `Args` internal; `PilotToolAttribute` shipped but unimplemented |
| Core | Accept-loop 10/s forever on pipe-name collision |
| Core | StartupHook clears entire `DOTNET_STARTUP_HOOKS` |
| Core | Non-atomic discovery write; crash leaves stale files |
| Core | Unbounded shared startup-hook log |
| Adapters | Dead `Unsupported` helper; highlight duration uncapped on WPF/Avalonia |
| Adapters | Overlap reporting sibling-only on WinForms vs all-pairs elsewhere |
| Adapters | Focus ignores returned bool; WinForms reflective `OnClick` |
| CLI | Dead `ProcessJob` name param; restart race; `start_process` overrides active session |
| CLI | `.exe` vs `.dll` argument quoting divergence; dead `RpcCode` |
| Tooling | Backup files never pruned; WiX ICE suppressions undocumented |
| Tooling | `smoke-test` leaks `UIPILOT_ENABLE`; GC.Collect prune test fragility |
| Tooling | Package version mismatches; no analyzers; `rollForward: latestMajor` |

---

## Adapter behavior matrix (same MCP tool, different results)

| Capability | WPF | Avalonia | WinForms |
|------------|-----|----------|----------|
| `press_keys` | In-process; modifiers ineffective | In-process; modifiers work | Global `SendKeys` to focused window |
| `scroll(dx,dy)` | Raw wheel delta; vertical only | Scroll lines; null pointer risk | Raw delta; HWheel OK |
| `click` | UIA peers + fallbacks | 5 hardcoded types; command skips Click | Reflective `OnClick` on anything |
| `type_text` | Errors on non-text | TextBox/password | Sets `.Text` on anything |
| `screenshot` | DPI-aware; minimized OK | 96 DPI; bitmap leak | Client/non-client clip; minimized weak |
| `get_binding_errors` | Real | Values often lost | Always empty |
| Coordinates | Mixed physical/logical | Mixed + centre wrong | All physical |

---

## Test coverage gaps

**Zero unit coverage (product surface):**

- All of `UiPilot.Wpf` (~11 files)
- All of `UiPilot.Avalonia` (~10 files)
- All of `UiPilot.WinForms` (~10 files)
- `UiPilot.StartupHook` framework-detection matrix
- `Core`: `RealInput`, `PipeIntegrity`, `ToolContext`
- `Cli`: `OperationTelemetry`, most of Forwarding/Lifecycle behavior
- `Client`: `ToolResults` parsing

**Weak / surface-only:** CLI tool name reflection tests; single happy-path E2Es that no-op off Windows and break under Release.

---

## Docs vs code drift

- `docs/06-protocol.md` - `uiFramework` listed as wpf/avalonia only; WinForms exists.
- Same doc advertises 4 concurrent clients; Core serializes all tool work.
- `docs/04-security.md` - omits StartupHook `force: true` path; overstates discovery cleanup on crash.

---

## Done well (keep)

1. Clean `IUiBackend` / thin `PilotHost` wrappers; framework types do not leak.
2. `ElementRegistry` weak-handle design (`ConditionalWeakTable` + pruning).
3. `PipeSessionAuth` exact-byte framing (no `StreamReader` corruption of MCP).
4. `PipeIntegrity` careful Win32 + graceful fallback.
5. `ProcessJob` documented Windows process-tree rationale.
6. Status service: loopback-only, fixed-time token compare, secret-free projections + tests.
7. Extension generation-counter reconnect design.
8. Installer JSON-merge semantics tested on Windows and Linux (when version parse is fixed).

---

## Recommended fix order (when remediating)

1. WinForms `SendKeys` -> in-process keys or typed `unsupported` (**C1**)
2. Fix Linux version extraction (**C2**)
3. Per-call cancellation; drop global invoke gate; `OnUi` timeout
4. Thread-safe `ToolRegistry`
5. Auth deadline + line-length cap (both sides)
6. CLI: dictionary lock + per-session state machines; fix `SendAsync` race
7. Stop killing attach-only PIDs on session rename
8. Typed `StaleElement` + real MCP input schemas
9. One coordinate unit + one scroll/keys semantic; extract shared adapter logic into Core
10. Port or delete `scripts/`; config-aware test paths; trait-based test categories

---

## Feature / improvement ideas (not bugs - for later discussion)

- Capability discovery on `IUiBackend` so agents know which tools work per framework.
- Attribute-based custom tool discovery (`PilotToolAttribute`) actually implemented.
- Soft timeouts / progress notifications for long `wait_*` tools.
- Protocol version negotiation with clear CLI <-> app mismatch errors.
- Linux/macOS real-input path (or explicit platform unsupported UX).
- Coverage gate (coverlet) and analyzer/`TreatWarningsAsErrors` in CI.
- Single source of truth for status port / SDK floor across C#, installers, extension.

---

*End of findings document.*