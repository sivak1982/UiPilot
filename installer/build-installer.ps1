<#
    Builds a framework-dependent, per-user UiPilot Windows installer bundle.
    The generated ZIP contains install.ps1, uninstall.ps1, and the CLI payload.
#>
[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDirectory = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1")

Assert-UiPilotWindows
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
$bundleRoot = Join-Path $OutputDirectory $bundleName
$payloadDirectory = Join-Path $bundleRoot "payload"
$archivePath = Join-Path $OutputDirectory "$bundleName.zip"

Write-Host "Using .NET SDK $sdk"
Write-Host "Building UiPilot $version for $RuntimeIdentifier"

if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null

if (-not $SkipTests) {
    & dotnet test (Join-Path $repoRoot "UiPilot.sln") --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "UiPilot tests failed."
    }
}

& dotnet publish (Join-Path $repoRoot "src\UiPilot.Cli\UiPilot.Cli.csproj") `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained false `
    --output $payloadDirectory `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "UiPilot.Cli publish failed."
}

$requiredFiles = @(
    (Join-Path $payloadDirectory "UiPilot.Cli.exe"),
    (Join-Path $payloadDirectory "UiPilot.Cli.dll"),
    (Join-Path $payloadDirectory "hooks\wpf\UiPilot.Wpf.StartupHook.dll"),
    (Join-Path $payloadDirectory "hooks\avalonia\UiPilot.Avalonia.StartupHook.dll")
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Published installer payload is incomplete; missing '$requiredFile'."
    }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install.ps1") -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $bundleRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1") -Destination $bundleRoot

@"
UiPilot $version ($RuntimeIdentifier)

Install for the current user:
  powershell -ExecutionPolicy Bypass -File .\install.ps1

The installer checks Windows, PowerShell, and the required .NET 10 runtime. It installs
UiPilot under %LOCALAPPDATA%\Programs\UiPilot and registers the MCP server in Cursor.
"@ | Set-Content -LiteralPath (Join-Path $bundleRoot "README.txt") -Encoding UTF8

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $archivePath -CompressionLevel Optimal

Write-Host ""
Write-Host "Installer bundle: $archivePath" -ForegroundColor Green
Write-Host "Extract it and run install.ps1."
