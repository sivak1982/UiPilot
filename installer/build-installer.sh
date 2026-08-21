#!/usr/bin/env bash
# Builds the Linux UiPilot ZIP (framework-dependent CLI, hooks, VSIX, install scripts).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# shellcheck source=linux/UiPilot.Installer.Common.sh
. "$SCRIPT_DIR/linux/UiPilot.Installer.Common.sh"

RID="${1:-linux-x64}"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_TESTS="${SKIP_TESTS:-0}"
case "$RID" in
  linux-x64|linux-arm64) ;;
  *) uipilot_die "supported RIDs: linux-x64, linux-arm64" ;;
esac

uipilot_require_linux
uipilot_assert_build_sdk

VERSION="$(python3 - <<PY
import xml.etree.ElementTree as ET
root = ET.parse("$REPO_ROOT/Directory.Build.props").getroot()
# Local name must be exactly Version (not LangVersion / PackageVersion / etc.).
print(next(
    node.text for node in root.iter()
    if node.tag.rsplit("}", 1)[-1] == "Version" and node.text and node.text.strip()
))
PY
)"
BUILD_NUMBER="${BUILD_NUMBER:-${BUILD_BUILDID:-${GITHUB_RUN_NUMBER:-0}}}"
[[ "$BUILD_NUMBER" =~ ^[0-9]+$ ]] || uipilot_die "build number must be a non-negative integer"
FULL_VERSION="$VERSION.$BUILD_NUMBER"
# NuGet normalizes a trailing zero revision (0.1.0.0 -> 0.1.0) in package identities.
PACKAGE_VERSION="$FULL_VERSION"
if [[ "$BUILD_NUMBER" == "0" ]]; then
  PACKAGE_VERSION="$VERSION"
fi
OUT_DIR="${OUTPUT_DIRECTORY:-$REPO_ROOT/artifacts/installer}"
BUNDLE_NAME="UiPilot-$FULL_VERSION-$RID"
BUNDLE_ROOT="$OUT_DIR/$BUNDLE_NAME"
PAYLOAD="$BUNDLE_ROOT/payload"
ARCHIVE="$OUT_DIR/$BUNDLE_NAME.zip"
VSIX="$BUNDLE_ROOT/UiPilot.Status.vsix"
EXT="$REPO_ROOT/extensions/uipilot-status"

rm -rf "$BUNDLE_ROOT"
mkdir -p "$PAYLOAD" "$OUT_DIR"

pushd "$EXT" >/dev/null
npm ci
if [[ "$SKIP_TESTS" != "1" ]]; then
  npm test
fi
npm run build
npx --no-install vsce package --no-dependencies --allow-missing-repository --out "$VSIX"
popd >/dev/null

if [[ "$SKIP_TESTS" != "1" ]]; then
  dotnet test "$REPO_ROOT/UiPilot.sln" --configuration "$CONFIGURATION" --nologo
  bash "$SCRIPT_DIR/linux/tests/installer-common-tests.sh"
fi

dotnet publish "$REPO_ROOT/src/UiPilot.Cli/UiPilot.Cli.csproj" \
  --configuration "$CONFIGURATION" \
  --runtime "$RID" \
  --self-contained false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:BuildNumber="$BUILD_NUMBER" \
  --output "$PAYLOAD" \
  --nologo
printf '%s\n' "$FULL_VERSION" > "$PAYLOAD/version.txt"

PACKAGES="$PAYLOAD/packages"
mkdir -p "$PACKAGES"
for project in UiPilot.Core UiPilot.Client; do
  dotnet pack "$REPO_ROOT/src/$project/$project.csproj" \
    --configuration "$CONFIGURATION" \
    --output "$PACKAGES" \
    -p:BuildNumber="$BUILD_NUMBER" \
    -p:Version="$PACKAGE_VERSION" \
    -p:PackageVersion="$PACKAGE_VERSION" \
    --nologo
done
mkdir -p "$PAYLOAD/skills/uipilot-csharp-tests"
cp "$REPO_ROOT/.cursor/skills/uipilot-csharp-tests/SKILL.md" "$PAYLOAD/skills/uipilot-csharp-tests/SKILL.md"

find "$PAYLOAD" -name '*.pdb' -delete
chmod +x "$PAYLOAD/UiPilot.Cli"

required=(
  "$PAYLOAD/UiPilot.Cli"
  "$PAYLOAD/UiPilot.Cli.dll"
  "$PAYLOAD/version.txt"
  "$PAYLOAD/hooks/UiPilot.StartupHook.dll"
  "$PAYLOAD/hooks/avalonia/UiPilot.Avalonia.dll"
  "$PACKAGES/UiPilot.Client.$PACKAGE_VERSION.nupkg"
  "$PACKAGES/UiPilot.Core.$PACKAGE_VERSION.nupkg"
  "$PAYLOAD/skills/uipilot-csharp-tests/SKILL.md"
  "$VSIX"
)
for file in "${required[@]}"; do
  [[ -f "$file" ]] || uipilot_die "published installer payload is incomplete; missing '$file'"
done

cp "$SCRIPT_DIR/linux/install.sh" "$BUNDLE_ROOT/"
cp "$SCRIPT_DIR/linux/uninstall.sh" "$BUNDLE_ROOT/"
cp "$SCRIPT_DIR/linux/UiPilot.Installer.Common.sh" "$BUNDLE_ROOT/"
chmod +x "$BUNDLE_ROOT/install.sh" "$BUNDLE_ROOT/uninstall.sh"

cat > "$BUNDLE_ROOT/README.txt" <<EOF
UiPilot $FULL_VERSION ($RID)

Extract this ZIP and run:
  chmod +x install.sh uninstall.sh payload/UiPilot.Cli
  ./install.sh

Requires Microsoft.NETCore.App 8.0 or later and python3. The installer copies UiPilot to
~/.local/share/UiPilot, registers the Cursor MCP server, and installs the Status extension
when the Cursor CLI is available. Linux can drive Avalonia applications; WPF and WinForms
automation require Windows.
EOF

rm -f "$ARCHIVE"
(
  cd "$OUT_DIR"
  python3 - <<PY
import pathlib, zipfile
bundle = pathlib.Path("$BUNDLE_NAME")
archive = pathlib.Path("$BUNDLE_NAME.zip")
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as zf:
    for path in bundle.rglob("*"):
        if path.is_file():
            zf.write(path, path.relative_to(bundle.parent))
print(archive.resolve())
PY
)

echo
echo "Installer bundle: $ARCHIVE"
