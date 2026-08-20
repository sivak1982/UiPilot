<#
    Builds a framework-dependent UiPilot installer.
    Windows RIDs produce a per-user MSI. Linux RIDs produce a ZIP with install scripts.
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1")

$isWindowsRid = $RuntimeIdentifier.StartsWith("win-")
if ($isWindowsRid) {
    Assert-UiPilotWindows
}
$sdk = Assert-UiPilotBuildSdk

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\installer"
}
else {
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
}

[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Raw
$versionNode = $buildProps.SelectSingleNode("/Project/PropertyGroup/Version")
$version = if ($null -ne $versionNode) { [string]$versionNode.InnerText } else { "" }
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read UiPilot's version from Directory.Build.props."
}

$bundleName = "UiPilot-$version-$RuntimeIdentifier"
# Windows uses this only as MSI staging; Linux ships it as the ZIP bundle.
$bundleRoot = Join-Path $OutputDirectory $bundleName
$payloadDirectory = Join-Path $bundleRoot "payload"
$archivePath = Join-Path $OutputDirectory "$bundleName.zip"
$msiPath = Join-Path $OutputDirectory "$bundleName.msi"
$extensionDirectory = Join-Path $repoRoot "extensions\uipilot-status"
$extensionVsix = Join-Path $bundleRoot "UiPilot.Status.vsix"

Write-Host "Using .NET SDK $sdk"
Write-Host "Building UiPilot $version for $RuntimeIdentifier"

if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null

$npm = if (Get-Command npm.cmd -ErrorAction SilentlyContinue) { "npm.cmd" } else { "npm" }
$npx = if (Get-Command npx.cmd -ErrorAction SilentlyContinue) { "npx.cmd" } else { "npx" }

Push-Location $extensionDirectory
try {
    & $npm ci
    if ($LASTEXITCODE -ne 0) {
        throw "Cursor extension dependency installation failed."
    }

    if (-not $SkipTests) {
        & $npm test
        if ($LASTEXITCODE -ne 0) {
            throw "Cursor extension tests failed."
        }
    }

    & $npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "Cursor extension build failed."
    }

    & $npx --no-install vsce package --no-dependencies --allow-missing-repository --out $extensionVsix
    if ($LASTEXITCODE -ne 0) {
        throw "Cursor extension VSIX packaging failed."
    }
}
finally {
    Pop-Location
}

if (-not $SkipTests) {
    & dotnet test (Join-Path $repoRoot "UiPilot.sln") --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "UiPilot tests failed."
    }

    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot "tests\Installer.Common.Tests.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "UiPilot installer tests failed."
    }
}

& dotnet publish (Join-Path $repoRoot "src\UiPilot.Cli\UiPilot.Cli.csproj") `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $payloadDirectory `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "UiPilot.Cli publish failed."
}

Get-ChildItem -LiteralPath $payloadDirectory -Filter *.pdb -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force

if ($isWindowsRid) {
    # The MSI installs the payload verbatim, so Cursor registration ships inside it.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Register-Cursor.ps1") -Destination $payloadDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1") -Destination $payloadDirectory
    Copy-Item -LiteralPath $extensionVsix -Destination (Join-Path $payloadDirectory "UiPilot.Status.vsix")
}

$cliName = if ($isWindowsRid) { "UiPilot.Cli.exe" } else { "UiPilot.Cli" }
$requiredFiles = @(
    (Join-Path $payloadDirectory $cliName),
    (Join-Path $payloadDirectory "UiPilot.Cli.dll"),
    (Join-Path $payloadDirectory "hooks\UiPilot.StartupHook.dll"),
    (Join-Path $payloadDirectory "hooks\avalonia\UiPilot.Avalonia.dll"),
    $extensionVsix
)
if ($isWindowsRid) {
    $requiredFiles += @(
        (Join-Path $payloadDirectory "hooks\wpf\UiPilot.Wpf.dll"),
        (Join-Path $payloadDirectory "hooks\winforms\UiPilot.WinForms.dll"),
        (Join-Path $payloadDirectory "Register-Cursor.ps1")
    )
}
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Published installer payload is incomplete; missing '$requiredFile'."
    }
}

if (-not $isWindowsRid) {
    $cliPath = Join-Path $payloadDirectory "UiPilot.Cli"
    if (Test-Path -LiteralPath $cliPath) {
        $chmod = Get-Command chmod -ErrorAction SilentlyContinue
        if ($chmod) {
            & $chmod.Source +x $cliPath
        }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "linux\install.sh") -Destination $bundleRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "linux\uninstall.sh") -Destination $bundleRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "linux\UiPilot.Installer.Common.sh") -Destination $bundleRoot

    @"
UiPilot $version ($RuntimeIdentifier)

Extract this ZIP and run:
  chmod +x install.sh uninstall.sh payload/UiPilot.Cli
  ./install.sh

Requires Microsoft.NETCore.App 8.0 or later and python3. The installer copies UiPilot to
~/.local/share/UiPilot, registers the Cursor MCP server, and installs the Status extension
when the Cursor CLI is available. Linux can drive Avalonia applications; WPF and WinForms
automation require Windows.
"@ | Set-Content -LiteralPath (Join-Path $bundleRoot "README.txt") -Encoding UTF8
}

if ($isWindowsRid) {
    $wixPlatform = if ($RuntimeIdentifier -eq "win-arm64") { "arm64" } else { "x64" }
    $wixOut = Join-Path $OutputDirectory "wix-$RuntimeIdentifier"
    if (Test-Path -LiteralPath $wixOut) {
        Remove-Item -LiteralPath $wixOut -Recurse -Force
    }
    New-Item -ItemType Directory -Path $wixOut -Force | Out-Null

    $payloadWxs = Join-Path $wixOut "PayloadComponents.wxs"
    Write-UiPilotWixPayloadComponents -PayloadDirectory $payloadDirectory -OutputPath $payloadWxs

    & dotnet build (Join-Path $PSScriptRoot "wix\UiPilot.Installer.wixproj") `
        -c Release `
        -p:InstallerPlatform=$wixPlatform `
        -p:ProductVersion=$version `
        -p:PayloadWxs=$payloadWxs `
        -p:OutputPath=$wixOut\ `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "UiPilot MSI build failed."
    }

    $builtMsi = Get-ChildItem -LiteralPath $wixOut -Filter *.msi | Select-Object -First 1
    if ($null -eq $builtMsi) {
        throw "WiX build did not produce an MSI."
    }
    Copy-Item -LiteralPath $builtMsi.FullName -Destination $msiPath -Force

    Write-Host ""
    Write-Host "Windows installer: $msiPath" -ForegroundColor Green
    Write-Host "Install it with 'msiexec /i `"$msiPath`"' or by double-clicking it."
}
else {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

    Write-Host ""
    Write-Host "Linux installer bundle: $archivePath" -ForegroundColor Green
    Write-Host "Extract it and run ./install.sh."
}
