# UiPilot Status

A read-only Cursor/VS Code extension for monitoring the local UiPilot CLI status service.

Configure `uipilotStatus.token` to match `UIPILOT_STATUS_TOKEN`. The host is restricted to
`127.0.0.1`; the default port is `17831`.

The status bar appears after startup (`onStartupFinished`) without opening the sidebar. The
Activity Bar view shows connection state, sessions, current operations, and recent operations.
Click the status bar item to open that detailed view. The output channel is reserved for connection
diagnostics. Available commands refresh status, reconnect the monitor, open the detailed view, or
show diagnostics. The WebSocket monitor authenticates with `Authorization: Bearer` and never sends
control messages.
