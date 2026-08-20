# UiPilot installer

UiPilot ships a **per-user Windows MSI** and a **Linux ZIP**. The CLI targets `.NET 8` and rolls
forward onto .NET 8, 9, or 10 runtimes.

## Requirements

### Windows

- Windows 10 or later
- Windows PowerShell 5.1 or PowerShell 7+
- .NET 8 or later runtime (`Microsoft.NETCore.App`) for the installed CLI
- .NET SDK 8.0.400 or later to build installers
- Node.js/npm to build and package the Cursor extension
- Target WPF/Avalonia/WinForms applications must use .NET 8 or later for startup-hook injection

### Linux

- glibc `linux-x64` or `linux-arm64`
- .NET 8 or later runtime (`Microsoft.NETCore.App`)
- `python3` (used to merge Cursor JSON)
- Avalonia target apps only (WPF and WinForms automation require Windows)

The installer reports the matching `winget` command or download URL when a required .NET component
is absent. It does not silently install machine-wide prerequisites.

## Build

Windows MSI:

```powershell
.\installer\build-installer.ps1
```

Linux ZIP from Windows (cross-publish):

```powershell
.\installer\build-installer.ps1 -RuntimeIdentifier linux-x64
```

Linux ZIP on Linux:

```bash
./installer/build-installer.sh linux-x64
```

This runs the .NET, installer, and extension tests, publishes the CLI, packages the Status VSIX,
verifies the hook payloads, and writes artifacts under `artifacts/installer/`:

- `UiPilot-<version>-win-x64.msi`
- `UiPilot-<version>-linux-x64.zip` (when a Linux RID is selected)

Options:

```powershell
.\installer\build-installer.ps1 -RuntimeIdentifier win-arm64
.\installer\build-installer.ps1 -SkipTests
```

## Install (Windows)

Double-click the MSI, or:

```powershell
msiexec /i UiPilot-0.1.0-win-x64.msi
```

The MSI installs to `%LOCALAPPDATA%\Programs\UiPilot` and does not require administrator rights.
Reinstalling a newer version performs a major upgrade in place.

Existing Cursor MCP and user settings are preserved and backed up before modification. The
installer generates a random status token on first install, preserves it on reinstall, writes the
matching extension settings, and installs the bundled VSIX through the Cursor CLI when available.
If `cursor` is not on `PATH`, it prints the exact VSIX path for manual installation. Restart
Cursor after installation.

Cursor registration runs from `Register-Cursor.ps1` inside the install directory. To re-point an
installation at non-default Cursor configuration, run it directly:

```powershell
& "$env:LOCALAPPDATA\Programs\UiPilot\Register-Cursor.ps1" `
  -McpConfigPath C:\path\to\mcp.json `
  -CursorSettingsPath C:\path\to\settings.json
```

## Install (Linux)

```bash
chmod +x install.sh uninstall.sh payload/UiPilot.Cli
./install.sh
```

Default install directory: `${XDG_DATA_HOME:-$HOME/.local/share}/UiPilot`.
MCP config: `~/.cursor/mcp.json`.
Cursor settings: `${XDG_CONFIG_HOME:-$HOME/.config}/Cursor/User/settings.json`.

```bash
./install.sh --prefix "$HOME/tools/uipilot"
```

## Uninstall

Windows: use Apps & features, or

```powershell
msiexec /x UiPilot-0.1.0-win-x64.msi
```

Linux:

```bash
~/.local/share/UiPilot/uninstall.sh
```

Uninstall removes Cursor's `uipilot` entry only when it still points to this installation, so a
user-replaced MCP entry is not deleted. It intentionally does not remove the extension or user
settings automatically.
