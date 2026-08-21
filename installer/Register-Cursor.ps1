<#
    Registers or removes Cursor MCP/settings for an already-copied UiPilot install directory.
    Used by the MSI custom actions and can be run after a manual file copy.
#>
[CmdletBinding()]
param(
    [ValidateSet("Register", "Unregister")]
    [string]$Action = "Register",
    [string]$InstallDirectory = "",
    [string]$McpConfigPath = "",
    [string]$CursorSettingsPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "UiPilot.Installer.Common.ps1")

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = $PSScriptRoot
}
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)

$manifestPath = Get-UiPilotManifestPath -InstallDirectory $InstallDirectory

if ($Action -eq "Unregister") {
    $commandPath = Get-UiPilotInstalledCommandPath -InstallDirectory $InstallDirectory
    $manifest = $null
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Read-UiPilotJson -Path $manifestPath
    }
    if ([string]::IsNullOrWhiteSpace($McpConfigPath) -and $null -ne $manifest) {
        $McpConfigPath = [string]$manifest.mcpConfigPath
    }
    if ([string]::IsNullOrWhiteSpace($CursorSettingsPath) -and $null -ne $manifest) {
        $CursorSettingsPath = [string]$manifest.cursorSettingsPath
    }
    if ([string]::IsNullOrWhiteSpace($McpConfigPath)) {
        $McpConfigPath = Join-Path $HOME ".cursor\mcp.json"
    }

    $statusToken = Get-UiPilotStatusToken -ConfigPath $McpConfigPath
    $removed = Remove-UiPilotMcpServer -ConfigPath $McpConfigPath -InstalledCommandPath $commandPath
    if ($removed) {
        Write-Host "Removed UiPilot from Cursor's MCP configuration."
    }
    if (-not [string]::IsNullOrWhiteSpace($CursorSettingsPath)) {
        Remove-UiPilotExtensionSettings `
            -SettingsPath $CursorSettingsPath `
            -ExpectedToken $statusToken | Out-Null
    }
    Uninstall-UiPilotCursorExtension | Out-Null

    Unregister-UiPilotNugetSource -PackagesDirectory (Get-UiPilotPackagesDirectory -InstallDirectory $InstallDirectory) | Out-Null
    Uninstall-UiPilotCursorSkill | Out-Null

    # Windows Installer does not track this file, so it must be deleted here for the
    # install directory to be removed.
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
    }
    exit 0
}

Assert-UiPilotRuntime | Out-Null
Register-UiPilotCursorIntegration `
    -InstallDirectory $InstallDirectory `
    -McpConfigPath $McpConfigPath `
    -CursorSettingsPath $CursorSettingsPath
