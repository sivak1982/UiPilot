#!/usr/bin/env bash
# Removes the current user's Linux UiPilot installation and its Cursor MCP registration.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=UiPilot.Installer.Common.sh
. "$SCRIPT_DIR/UiPilot.Installer.Common.sh"

uipilot_require_linux
uipilot_require_python

INSTALL_DIR="${UIPILOT_INSTALL_DIR:-}"
MCP_CONFIG="${UIPILOT_MCP_CONFIG:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix) INSTALL_DIR="$2"; shift 2 ;;
    --mcp-config) MCP_CONFIG="$2"; shift 2 ;;
    -h|--help)
      echo "Usage: uninstall.sh [--prefix DIR] [--mcp-config PATH]"
      exit 0
      ;;
    *) uipilot_die "unknown argument: $1" ;;
  esac
done

if [[ -z "$INSTALL_DIR" && -f "$SCRIPT_DIR/install-manifest.json" ]]; then
  INSTALL_DIR="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("installDirectory") or "")' "$SCRIPT_DIR/install-manifest.json")"
  if [[ -z "$MCP_CONFIG" ]]; then
    MCP_CONFIG="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8")).get("mcpConfigPath") or "")' "$SCRIPT_DIR/install-manifest.json")"
  fi
fi

if [[ -z "$INSTALL_DIR" ]]; then
  INSTALL_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/UiPilot"
fi
if [[ -z "$MCP_CONFIG" ]]; then
  MCP_CONFIG="$HOME/.cursor/mcp.json"
fi

INSTALL_DIR="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$INSTALL_DIR")"
MCP_CONFIG="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$MCP_CONFIG")"
COMMAND="$INSTALL_DIR/UiPilot.Cli"

set +e
uipilot_remove_mcp "$MCP_CONFIG" "$COMMAND"
removed=$?
set -e
if [[ "$removed" -eq 0 ]]; then
  echo "Removed UiPilot from Cursor's MCP configuration."
fi

uipilot_unregister_nuget "$INSTALL_DIR/packages"
uipilot_uninstall_skill

if [[ -d "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  echo "Removed $INSTALL_DIR"
else
  echo "UiPilot is not installed at $INSTALL_DIR"
fi

echo "UiPilot uninstalled."
echo "Restart Cursor if it is currently running."
