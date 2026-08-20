#!/usr/bin/env bash
# JSON merge smoke tests for the Linux installer helpers.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=../UiPilot.Installer.Common.sh
. "$ROOT/UiPilot.Installer.Common.sh"

uipilot_require_python

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
MCP="$TMP/mcp.json"
SETTINGS="$TMP/settings.json"

printf '%s\n' '{"mcpServers":{"other":{"command":"other"}},"unrelated":true}' > "$MCP"
printf '%s\n' '{"editor.fontSize":15}' > "$SETTINGS"

TOKEN="$(uipilot_new_status_token)"
[[ ${#TOKEN} -eq 64 ]] || uipilot_die "generated token must be 64 hex characters"

uipilot_merge_mcp "$MCP" "/tmp/UiPilot/UiPilot.Cli" "$TOKEN" "0.1.0.42"
uipilot_merge_settings "$SETTINGS" "$TOKEN"

python3 - "$MCP" "$SETTINGS" "$TOKEN" <<'PY'
import json, sys
mcp_path, settings_path, token = sys.argv[1:]
mcp = json.load(open(mcp_path, encoding="utf-8"))
settings = json.load(open(settings_path, encoding="utf-8"))
assert mcp["mcpServers"]["other"]["command"] == "other"
assert mcp["unrelated"] is True
assert mcp["mcpServers"]["uipilot-0.1.0.42"]["env"]["UIPILOT_STATUS_TOKEN"] == token
assert mcp["mcpServers"]["uipilot-0.1.0.42"]["env"]["UIPILOT_STATUS_PORT"] == "17831"
assert settings["editor.fontSize"] == 15
assert settings["uipilotStatus.host"] == "127.0.0.1"
assert settings["uipilotStatus.port"] == 17831
assert settings["uipilotStatus.token"] == token
PY

python3 -c 'import json,sys; d=json.load(open(sys.argv[1], encoding="utf-8")); d["mcpServers"]["uipilot-0.1.0.42"]["env"]["CUSTOM_ENV"]="keep-me"; json.dump(d, open(sys.argv[1],"w"))' "$MCP"
uipilot_merge_mcp "$MCP" "/tmp/UiPilot/UiPilot.Cli" "$TOKEN" "0.1.0.43"
PRESERVED="$(uipilot_read_status_token "$MCP")"
[[ "$PRESERVED" == "$TOKEN" ]] || uipilot_die "reinstall must preserve the existing status token"
python3 - "$MCP" <<'PY'
import json, sys
mcp = json.load(open(sys.argv[1], encoding="utf-8"))
assert "uipilot-0.1.0.42" not in mcp["mcpServers"]
assert mcp["mcpServers"]["uipilot-0.1.0.43"]["env"]["CUSTOM_ENV"] == "keep-me"
PY

uipilot_remove_mcp "$MCP" "/tmp/UiPilot/UiPilot.Cli"
python3 - "$MCP" <<'PY'
import json, sys
mcp = json.load(open(sys.argv[1], encoding="utf-8"))
assert "uipilot-0.1.0.43" not in mcp["mcpServers"]
assert mcp["mcpServers"]["other"]["command"] == "other"
PY

echo "Linux installer common tests passed."
