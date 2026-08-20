#!/usr/bin/env bash
# Shared helpers for the Linux UiPilot installer.
set -euo pipefail

UIPILOT_REQUIRED_RUNTIME_MAJOR=8
UIPILOT_STATUS_PORT=17831

uipilot_die() {
  echo "error: $*" >&2
  exit 1
}

uipilot_require_linux() {
  if [[ "$(uname -s)" != "Linux" ]]; then
    uipilot_die "this installer supports Linux only"
  fi
}

uipilot_require_python() {
  if ! command -v python3 >/dev/null 2>&1; then
    uipilot_die "python3 is required to merge Cursor JSON configuration"
  fi
}

uipilot_assert_runtime() {
  command -v dotnet >/dev/null 2>&1 || uipilot_die "dotnet is not on PATH. Install the .NET $UIPILOT_REQUIRED_RUNTIME_MAJOR runtime: https://dotnet.microsoft.com/download/dotnet/${UIPILOT_REQUIRED_RUNTIME_MAJOR}.0"
  local found=0
  while IFS= read -r line; do
    if [[ "$line" =~ ^Microsoft\.NETCore\.App[[:space:]]+([0-9]+)\. ]]; then
      if (( ${BASH_REMATCH[1]} >= UIPILOT_REQUIRED_RUNTIME_MAJOR )); then
        found=1
        break
      fi
    fi
  done < <(dotnet --list-runtimes)
  if [[ "$found" -ne 1 ]]; then
    uipilot_die "UiPilot.Cli requires Microsoft.NETCore.App ${UIPILOT_REQUIRED_RUNTIME_MAJOR}.0 or later. Install it from https://dotnet.microsoft.com/download/dotnet/${UIPILOT_REQUIRED_RUNTIME_MAJOR}.0"
  fi
}

uipilot_assert_build_sdk() {
  command -v dotnet >/dev/null 2>&1 || uipilot_die "dotnet SDK is not on PATH"
  local found=0
  while IFS= read -r line; do
    if [[ "$line" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+) ]]; then
      if (( ${BASH_REMATCH[1]} > 8 || ( ${BASH_REMATCH[1]} == 8 && ${BASH_REMATCH[2]} > 0 ) || ( ${BASH_REMATCH[1]} == 8 && ${BASH_REMATCH[2]} == 0 && ${BASH_REMATCH[3]} >= 400 ) )); then
        found=1
        break
      fi
    fi
  done < <(dotnet --list-sdks)
  if [[ "$found" -ne 1 ]]; then
    uipilot_die "Building UiPilot requires .NET SDK 8.0.400 or later"
  fi
}

uipilot_new_status_token() {
  python3 - <<'PY'
import secrets
print(secrets.token_hex(32))
PY
}

uipilot_merge_mcp() {
  local config_path="$1"
  local command_path="$2"
  local status_token="$3"
  local version="$4"
  local status_port="${5:-$UIPILOT_STATUS_PORT}"
  python3 - "$config_path" "$command_path" "$status_token" "$version" "$status_port" <<'PY'
import json, os, re, re, shutil, sys, datetime

def loads_jsonc(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"(?<!:)//.*?$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return loads_jsonc(text)
path, command, token, version, port = sys.argv[1:]
parts = version.split(".")
if len(parts) != 4 or not all(part.isdigit() for part in parts):
    raise SystemExit(f"UiPilot MCP version '{version}' must use major.minor.patch.build format")
server_name = f"uipilot-{version}"
data = {}
if os.path.isfile(path):
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read().strip()
        if text:
            data = loads_jsonc(text)
    backup = f"{path}.backup-{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}"
    shutil.copy2(path, backup)
    print(f"Backed up existing JSON configuration to {backup}")
servers = data.setdefault("mcpServers", {})
uipilot_names = [
    name for name in servers
    if name == "uipilot" or name.startswith("uipilot-")
] if isinstance(servers, dict) else []
existing = servers.get(server_name)
if not isinstance(existing, dict) and uipilot_names:
    existing = servers.get(uipilot_names[0])
env = {}
if isinstance(existing, dict) and isinstance(existing.get("env"), dict):
    env.update(existing["env"])
env["UIPILOT_STATUS_PORT"] = str(port)
env["UIPILOT_STATUS_TOKEN"] = token
for name in uipilot_names:
    configured = str((servers.get(name) or {}).get("command") or "")
    if name == "uipilot" or os.path.normpath(configured) == os.path.normpath(command):
        del servers[name]
servers[server_name] = {"command": command, "args": [], "env": env}
os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(data, handle, indent=2)
    handle.write("\n")
PY
}

uipilot_read_status_token() {
  local config_path="$1"
  python3 - "$config_path" <<'PY'
import json, os, re, sys
def loads_jsonc(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"(?<!:)//.*?$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return loads_jsonc(text)
path = sys.argv[1]
if not os.path.isfile(path):
    sys.exit(0)
with open(path, "r", encoding="utf-8") as handle:
    text = handle.read().strip()
    if not text:
        sys.exit(0)
    data = loads_jsonc(text)
servers = data.get("mcpServers") or {}
server = next((
    value for name, value in servers.items()
    if name == "uipilot" or name.startswith("uipilot-")
), {})
token = (server.get("env") or {}).get("UIPILOT_STATUS_TOKEN")
if token:
    print(token)
PY
}

uipilot_merge_settings() {
  local settings_path="$1"
  local status_token="$2"
  local status_port="${3:-$UIPILOT_STATUS_PORT}"
  python3 - "$settings_path" "$status_token" "$status_port" <<'PY'
import json, os, re, re, shutil, sys, datetime

def loads_jsonc(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"(?<!:)//.*?$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return loads_jsonc(text)
path, token, port = sys.argv[1:]
data = {}
if os.path.isfile(path):
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read().strip()
        if text:
            data = loads_jsonc(text)
    backup = f"{path}.backup-{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}"
    shutil.copy2(path, backup)
    print(f"Backed up existing JSON configuration to {backup}")
data["uipilotStatus.host"] = "127.0.0.1"
data["uipilotStatus.port"] = int(port)
data["uipilotStatus.token"] = token
os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(data, handle, indent=2)
    handle.write("\n")
PY
}

uipilot_remove_mcp() {
  local config_path="$1"
  local installed_command="$2"
  python3 - "$config_path" "$installed_command" <<'PY'
import json, os, re, re, shutil, sys, datetime

def loads_jsonc(text):
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"(?<!:)//.*?$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return loads_jsonc(text)
path, installed = sys.argv[1:]
if not os.path.isfile(path):
    sys.exit(1)
with open(path, "r", encoding="utf-8") as handle:
    data = loads_jsonc(handle.read() or "{}")
servers = data.get("mcpServers")
if not isinstance(servers, dict):
    sys.exit(1)
matching = []
for name, server in servers.items():
    if name != "uipilot" and not name.startswith("uipilot-"):
        continue
    command = str((server or {}).get("command") or "")
    if os.path.normpath(command) == os.path.normpath(installed):
        matching.append(name)
if not matching:
    print("warning: Cursor's UiPilot MCP entries point elsewhere; they were left unchanged.", file=sys.stderr)
    sys.exit(2)
backup = f"{path}.backup-{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}"
shutil.copy2(path, backup)
for name in matching:
    del servers[name]
with open(path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(data, handle, indent=2)
    handle.write("\n")
sys.exit(0)
PY
}

uipilot_install_extension() {
  local vsix_path="$1"
  if [[ ! -f "$vsix_path" ]]; then
    echo "warning: Cursor extension VSIX was not found at '$vsix_path'." >&2
    return 0
  fi
  if command -v cursor >/dev/null 2>&1; then
    if cursor --install-extension "$vsix_path" --force; then
      echo "Installed the UiPilot Status extension in Cursor."
      return 0
    fi
    echo "warning: Cursor CLI could not install the extension. In Cursor, use Extensions: Install from VSIX and select '$vsix_path'." >&2
    return 0
  fi
  echo "warning: Cursor CLI was not found. In Cursor, use Extensions: Install from VSIX and select '$vsix_path'." >&2
}

uipilot_register_nuget() {
  local packages="$1"
  command -v dotnet >/dev/null 2>&1 || return 0
  [[ -d "$packages" ]] || return 0
  shopt -s nullglob
  local nupkgs=("$packages"/*.nupkg)
  shopt -u nullglob
  [[ ${#nupkgs[@]} -gt 0 ]] || return 0
  dotnet nuget remove source UiPilotInstalled >/dev/null 2>&1 || true
  if dotnet nuget add source "$packages" --name UiPilotInstalled; then
    echo "Registered NuGet source 'UiPilotInstalled' -> $packages"
  fi
}

uipilot_unregister_nuget() {
  local packages="$1"
  command -v dotnet >/dev/null 2>&1 || return 0
  local listed
  listed="$(dotnet nuget list source 2>/dev/null || true)"
  [[ "$listed" == *"$packages"* ]] || return 0
  dotnet nuget remove source UiPilotInstalled >/dev/null 2>&1 || true
}

uipilot_install_skill() {
  local install_dir="$1"
  local src="$install_dir/skills/uipilot-csharp-tests/SKILL.md"
  [[ -f "$src" ]] || return 0
  local dest="$HOME/.cursor/skills/uipilot-csharp-tests"
  mkdir -p "$dest"
  cp "$src" "$dest/SKILL.md"
  echo "Installed the UiPilot tester skill at $dest"
}

uipilot_uninstall_skill() {
  local dest="$HOME/.cursor/skills/uipilot-csharp-tests"
  [[ -d "$dest" ]] || return 0
  rm -rf "$dest"
}
