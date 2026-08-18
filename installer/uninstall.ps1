<#
    Removes the current user's UiPilot installation and its Cursor MCP registration.
#>
[CmdletBinding()]
param(
    [string]$InstallDirectory = "",
    [string]$McpConfigPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1")

Assert-UiPilotWindows

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $localManifest = Join-Path $PSScriptRoot "install-manifest.json"
    if (Test-Path -LiteralPath $localManifest) {
        $manifest = Read-UiPilotJson -Path $localManifest
        $InstallDirectory = [string]$manifest.installDirectory
        if ([string]::IsNullOrWhiteSpace($McpConfigPath)) {
            $McpConfigPath = [string]$manifest.mcpConfigPath
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\UiPilot"
    }
    else {
        throw "LOCALAPPDATA is not defined; specify -InstallDirectory explicitly."
    }
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)

if ([string]::IsNullOrWhiteSpace($McpConfigPath)) {
    if ([string]::IsNullOrWhiteSpace($HOME)) {
        throw "The current user's home directory is unavailable; specify -McpConfigPath explicitly."
    }
    $McpConfigPath = Join-Path $HOME ".cursor\mcp.json"
}
$McpConfigPath = [IO.Path]::GetFullPath($McpConfigPath)

$installedCommand = Join-Path $InstallDirectory "UiPilot.Cli.exe"
$removedRegistration = Remove-UiPilotMcpServer `
    -ConfigPath $McpConfigPath `
    -InstalledCommandPath $installedCommand

if (Test-Path -LiteralPath $InstallDirectory) {
    $currentDirectory = [IO.Path]::GetFullPath((Get-Location).Path)
    $installPrefix = $InstallDirectory.TrimEnd("\") + "\"
    if ($currentDirectory.Equals($InstallDirectory, [StringComparison]::OrdinalIgnoreCase) -or
        $currentDirectory.StartsWith($installPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Set-Location $env:TEMP
    }

    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    Write-Host "Removed $InstallDirectory"
}
else {
    Write-Host "UiPilot is not installed at $InstallDirectory"
}

if ($removedRegistration) {
    Write-Host "Removed UiPilot from Cursor's MCP configuration."
}

Write-Host "UiPilot uninstalled." -ForegroundColor Green
Write-Host "Restart Cursor if it is currently running."
