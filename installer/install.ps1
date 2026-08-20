<#
    Installs UiPilot for the current Windows user and registers its Cursor MCP server.
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = "",
    [string]$McpConfigPath = "",
    [string]$PayloadDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1")

Assert-UiPilotWindows
$runtime = Assert-UiPilotRuntime

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw "LOCALAPPDATA is not defined; specify -InstallDirectory explicitly."
    }
    $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\UiPilot"
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)

if ([string]::IsNullOrWhiteSpace($McpConfigPath)) {
    if ([string]::IsNullOrWhiteSpace($HOME)) {
        throw "The current user's home directory is unavailable; specify -McpConfigPath explicitly."
    }
    $McpConfigPath = Join-Path $HOME ".cursor\mcp.json"
}
$McpConfigPath = [IO.Path]::GetFullPath($McpConfigPath)

if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
    $PayloadDirectory = Join-Path $PSScriptRoot "payload"
}
$PayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)

$requiredPayload = @(
    (Join-Path $PayloadDirectory "UiPilot.Cli.exe"),
    (Join-Path $PayloadDirectory "UiPilot.Cli.dll"),
    (Join-Path $PayloadDirectory "hooks\UiPilot.StartupHook.dll"),
    (Join-Path $PayloadDirectory "hooks\wpf\UiPilot.Wpf.dll"),
    (Join-Path $PayloadDirectory "hooks\avalonia\UiPilot.Avalonia.dll"),
    (Join-Path $PayloadDirectory "hooks\winforms\UiPilot.WinForms.dll")
)
foreach ($requiredFile in $requiredPayload) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Installer payload is incomplete; missing '$requiredFile'. Re-extract the installer ZIP and try again."
    }
}

# Parse the existing file before changing the installation, so malformed user JSON cannot
# leave a successful file install with an unregistered MCP server.
$null = Read-UiPilotJson -Path $McpConfigPath

$installParent = Split-Path -Parent $InstallDirectory
New-Item -ItemType Directory -Path $installParent -Force | Out-Null
$stagingDirectory = "$InstallDirectory.installing-$([Guid]::NewGuid().ToString('N'))"

Write-Host "Using .NET runtime $runtime"
Write-Host "Installing UiPilot to $InstallDirectory"

try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $PayloadDirectory "*") -Destination $stagingDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $stagingDirectory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1") -Destination $stagingDirectory -Force

    $installedCommand = Join-Path $InstallDirectory "UiPilot.Cli.exe"
    $manifest = [pscustomobject]@{
        installedAtUtc = [DateTime]::UtcNow.ToString("o")
        installDirectory = $InstallDirectory
        mcpConfigPath = $McpConfigPath
        command = $installedCommand
        requiredRuntime = "$script:UiPilotRequiredRuntimeMajor.0"
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        (Join-Path $stagingDirectory "install-manifest.json"),
        $manifestJson + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))

    if (Test-Path -LiteralPath $InstallDirectory) {
        Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $InstallDirectory

    Set-UiPilotMcpServer -ConfigPath $McpConfigPath -CommandPath $installedCommand
}
catch {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    throw
}

Write-Host ""
Write-Host "UiPilot installed and registered with Cursor." -ForegroundColor Green
Write-Host "MCP configuration: $McpConfigPath"
Write-Host "Restart Cursor to load the UiPilot MCP server."
Write-Host "Target applications must use .NET 8 or later for startup-hook injection."
