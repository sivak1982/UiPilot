# UiPilot Windows installer

The installer is per-user and does not require administrator rights. It installs the
framework-dependent CLI under `%LOCALAPPDATA%\Programs\UiPilot`, keeps the generic startup hook
and adapter payloads beside it, and merges an `uipilot` entry into
`%USERPROFILE%\.cursor\mcp.json`.

## Requirements

- Windows 10 or later
- Windows PowerShell 5.1 or PowerShell 7+
- .NET 10 runtime (`Microsoft.NETCore.App`) for the installed CLI
- .NET SDK 10.0.301 or newer in the .NET 10 line to build the installer
- Node.js/npm to build and package the Cursor extension
- Target WPF/Avalonia applications must use .NET 8 or later for startup-hook injection

The scripts report the matching `winget` command and download URL when a required .NET component
is absent. They do not silently install machine-wide prerequisites.

## Build

```powershell
.\installer\build-installer.ps1
```

This runs the .NET, installer, and extension tests, publishes the CLI, packages the Status VSIX,
verifies the hook payloads, and creates
`artifacts\installer\UiPilot-<version>-win-x64.zip`.

Options:

```powershell
.\installer\build-installer.ps1 -RuntimeIdentifier win-arm64
.\installer\build-installer.ps1 -SkipTests
```

## Install

Extract the generated ZIP, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Existing Cursor MCP and user settings are preserved and backed up before modification. The
installer generates a random status token on first install, preserves it on reinstall, writes the
matching extension settings, and installs the bundled VSIX through the Cursor CLI when available.
If `cursor` is not on `PATH`, it prints the exact VSIX path for manual installation. Restart
Cursor after installation.

Custom locations are supported:

```powershell
.\install.ps1 `
  -InstallDirectory C:\Tools\UiPilot `
  -McpConfigPath C:\path\to\mcp.json `
  -CursorSettingsPath C:\path\to\settings.json
```

## Uninstall

Run the installed uninstaller:

```powershell
& "$env:LOCALAPPDATA\Programs\UiPilot\uninstall.ps1"
```

The uninstaller removes Cursor's `uipilot` entry only when it still points to this installation,
so a user-replaced MCP entry is not deleted. It intentionally does not remove the extension or
user settings automatically.
