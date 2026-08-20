#!/usr/bin/env bash
# Installs UiPilot for the current Linux user and registers its Cursor MCP server.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=UiPilot.Installer.Common.sh
. "$SCRIPT_DIR/UiPilot.Installer.Common.sh"

uipilot_require_linux
uipilot_require_python
uipilot_assert_runtime

INSTALL_DIR="${UIPILOT_INSTALL_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/UiPilot}"
MCP_CONFIG="${UIPILOT_MCP_CONFIG:-$HOME/.cursor/mcp.json}"
CURSOR_SETTINGS="${UIPILOT_CURSOR_SETTINGS:-${XDG_CONFIG_HOME:-$HOME/.config}/Cursor/User/settings.json}"
PAYLOAD_DIR="${UIPILOT_PAYLOAD_DIR:-$SCRIPT_DIR/payload}"
VSIX_PATH="${UIPILOT_VSIX:-$SCRIPT_DIR/UiPilot.Status.vsix}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) INSTALL_DIR="$2"; shift 2 ;;
    --mcp-config) MCP_CONFIG="$2"; shift 2 ;;
    --cursor-settings) CURSOR_SETTINGS="$2"; shift 2 ;;
    --payload) PAYLOAD_DIR="$2"; shift 2 ;;
    --vsix) VSIX_PATH="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: install.sh [--prefix DIR] [--mcp-config PATH] [--cursor-settings PATH]"
      exit 0
      ;;
    *) uipilot_die "unknown argument: $1" ;;
  esac
done

INSTALL_DIR="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$INSTALL_DIR")"
MCP_CONFIG="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$MCP_CONFIG")"
CURSOR_SETTINGS="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$CURSOR_SETTINGS")"
PAYLOAD_DIR="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$PAYLOAD_DIR")"
VSIX_PATH="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$VSIX_PATH")"

required=(
  "$PAYLOAD_DIR/UiPilot.Cli"
  "$PAYLOAD_DIR/UiPilot.Cli.dll"
  "$PAYLOAD_DIR/version.txt"
  "$PAYLOAD_DIR/hooks/UiPilot.StartupHook.dll"
  "$PAYLOAD_DIR/hooks/avalonia/UiPilot.Avalonia.dll"
)
for file in "${required[@]}"; do
  [[ -f "$file" ]] || uipilot_die "installer payload is incomplete; missing '$file'"
done
[[ -f "$VSIX_PATH" ]] || uipilot_die "installer bundle is incomplete; missing Cursor extension '$VSIX_PATH'"

TOKEN="$(uipilot_read_status_token "$MCP_CONFIG")"
if [[ -z "$TOKEN" ]]; then
  TOKEN="$(uipilot_new_status_token)"
fi
VERSION="$(tr -d '[:space:]' < "$PAYLOAD_DIR/version.txt")"

STAGING="$INSTALL_DIR.installing-$(python3 -c 'import uuid; print(uuid.uuid4().hex)')"
echo "Installing UiPilot to $INSTALL_DIR"
mkdir -p "$(dirname "$INSTALL_DIR")"
mkdir -p "$STAGING"
cp -a "$PAYLOAD_DIR"/. "$STAGING"/
cp -a "$SCRIPT_DIR/uninstall.sh" "$STAGING/"
cp -a "$SCRIPT_DIR/UiPilot.Installer.Common.sh" "$STAGING/"
cp -a "$VSIX_PATH" "$STAGING/UiPilot.Status.vsix"
chmod +x "$STAGING/UiPilot.Cli" "$STAGING/uninstall.sh"

python3 - "$STAGING/install-manifest.json" "$INSTALL_DIR" "$MCP_CONFIG" "$CURSOR_SETTINGS" "$VERSION" <<'PY'
import json, sys, datetime
path, install_dir, mcp, settings, version = sys.argv[1:]
manifest = {
    "installedAtUtc": datetime.datetime.utcnow().isoformat() + "Z",
    "installDirectory": install_dir,
    "mcpConfigPath": mcp,
    "cursorSettingsPath": settings,
    "command": f"{install_dir}/UiPilot.Cli",
    "mcpServerName": f"uipilot-{version}",
    "version": version,
    "requiredRuntime": "8.0",
}
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2)
    handle.write("\n")
PY

if [[ -e "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
fi
mv "$STAGING" "$INSTALL_DIR"

uipilot_merge_mcp "$MCP_CONFIG" "$INSTALL_DIR/UiPilot.Cli" "$TOKEN" "$VERSION"
uipilot_merge_settings "$CURSOR_SETTINGS" "$TOKEN"
uipilot_install_extension "$INSTALL_DIR/UiPilot.Status.vsix"
uipilot_register_nuget "$INSTALL_DIR/packages"
uipilot_install_skill "$INSTALL_DIR"

echo
echo "UiPilot installed and registered with Cursor."
echo "MCP configuration: $MCP_CONFIG"
echo "Cursor settings: $CURSOR_SETTINGS"
echo "Restart Cursor to load the UiPilot MCP server."
echo "Linux can drive Avalonia apps; WPF and WinForms automation require Windows."
