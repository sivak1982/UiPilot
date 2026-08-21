$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "..\UiPilot.Installer.Common.ps1")

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$root = Join-Path $env:TEMP "uipilot-installer-tests-$([Guid]::NewGuid().ToString('N'))"
$mcpPath = Join-Path $root "mcp.json"
$settingsPath = Join-Path $root "settings.json"

try {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    [IO.File]::WriteAllText(
        $mcpPath,
        '{"mcpServers":{"other":{"command":"other.exe"}},"unrelated":true}',
        (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText(
        $settingsPath,
        '{"editor.fontSize":15}',
        (New-Object Text.UTF8Encoding($false)))

    $token = Get-OrCreateUiPilotStatusToken -ConfigPath $mcpPath
    Assert-Equal 64 $token.Length "Generated status token must be 32 bytes of hex."

    $legacy = Read-UiPilotJson -Path $mcpPath
    $legacy.mcpServers | Add-Member -MemberType NoteProperty -Name "uipilot" -Value ([pscustomobject]@{
        command = "C:\UiPilot\UiPilot.Cli.exe"
        env = [pscustomobject]@{ UIPILOT_STATUS_TOKEN = $token }
    }) -Force
    $legacy.mcpServers | Add-Member -MemberType NoteProperty -Name "uipilot-0.0.9.1" -Value ([pscustomobject]@{
        command = "C:\OldUiPilot\UiPilot.Cli.exe"
        env = [pscustomobject]@{ UIPILOT_STATUS_TOKEN = "stale" }
    }) -Force
    Write-UiPilotJson -Path $mcpPath -Value $legacy

    Set-UiPilotMcpServer `
        -ConfigPath $mcpPath `
        -CommandPath "C:\UiPilot\UiPilot.Cli.exe" `
        -StatusToken $token `
        -Version "0.1.0.42"
    Set-UiPilotExtensionSettings `
        -SettingsPath $settingsPath `
        -StatusToken $token

    $mcp = Read-UiPilotJson -Path $mcpPath
    Assert-Equal "other.exe" $mcp.mcpServers.other.command "Unrelated MCP server was changed."
    Assert-Equal $true $mcp.unrelated "Unrelated MCP setting was changed."
    Assert-Equal $token $mcp.mcpServers."uipilot-0.1.0.42".env.UIPILOT_STATUS_TOKEN "MCP token mismatch."
    Assert-Equal "17831" $mcp.mcpServers."uipilot-0.1.0.42".env.UIPILOT_STATUS_PORT "MCP port mismatch."
    if ($null -ne $mcp.mcpServers.PSObject.Properties["uipilot"]) {
        throw "Registration must migrate the legacy unversioned MCP entry."
    }
    if ($null -ne $mcp.mcpServers.PSObject.Properties["uipilot-0.0.9.1"]) {
        throw "Registration must remove stale versioned MCP entries from old install paths."
    }

    $mcp.mcpServers."uipilot-0.1.0.42".env | Add-Member -MemberType NoteProperty -Name "CUSTOM_ENV" -Value "keep-me" -Force
    Write-UiPilotJson -Path $mcpPath -Value $mcp
    Set-UiPilotMcpServer `
        -ConfigPath $mcpPath `
        -CommandPath "C:\UiPilot\UiPilot.Cli.exe" `
        -StatusToken $token `
        -Version "0.1.0.43"
    $mcp = Read-UiPilotJson -Path $mcpPath
    Assert-Equal "keep-me" $mcp.mcpServers."uipilot-0.1.0.43".env.CUSTOM_ENV "Unrelated MCP environment was changed."
    if ($null -ne $mcp.mcpServers.PSObject.Properties["uipilot-0.1.0.42"]) {
        throw "Upgrade must remove the prior versioned MCP entry."
    }

    $settings = Read-UiPilotJson -Path $settingsPath
    Assert-Equal 15 $settings."editor.fontSize" "Unrelated Cursor setting was changed."
    Assert-Equal "127.0.0.1" $settings."uipilotStatus.host" "Extension host mismatch."
    Assert-Equal 17831 $settings."uipilotStatus.port" "Extension port mismatch."
    Assert-Equal $token $settings."uipilotStatus.token" "Extension token mismatch."

    $preserved = Get-OrCreateUiPilotStatusToken -ConfigPath $mcpPath
    Assert-Equal $token $preserved "Reinstall must preserve the existing status token."

    Assert-Equal 8 $script:UiPilotRequiredRuntimeMajor "Installed CLI must accept .NET 8 or later."
    Assert-Equal "8.0.400" $script:UiPilotRequiredSdkVersion.ToString() "Build SDK floor should be 8.0.400."

    $cursorCommand = Get-UiPilotCursorCommand
    if ($null -ne (Get-Command cursor -ErrorAction SilentlyContinue) -and
        [string]::IsNullOrWhiteSpace($cursorCommand)) {
        throw "Cursor CLI resolution failed even though Cursor is available."
    }

    $windowsCommand = Get-UiPilotInstalledCommandPath -InstallDirectory $root
    $expectedWindows = Join-Path $root "UiPilot.Cli"
    if ($windowsCommand -ne $expectedWindows) {
        throw "Missing exe should fall back to UiPilot.Cli. Expected '$expectedWindows', got '$windowsCommand'."
    }
    [IO.File]::WriteAllText((Join-Path $root "UiPilot.Cli.exe"), "")
    $windowsCommand = Get-UiPilotInstalledCommandPath -InstallDirectory $root
    $expectedExe = Join-Path $root "UiPilot.Cli.exe"
    Assert-Equal $expectedExe $windowsCommand "Windows installs should prefer UiPilot.Cli.exe."

    $harvestRoot = Join-Path $root "payload"
    New-Item -ItemType Directory -Path (Join-Path $harvestRoot "hooks") -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $harvestRoot "UiPilot.Cli.exe"), "")
    [IO.File]::WriteAllText((Join-Path $harvestRoot "hooks\UiPilot.StartupHook.dll"), "")
    $wxsPath = Join-Path $root "PayloadComponents.wxs"
    Write-UiPilotWixPayloadComponents -PayloadDirectory $harvestRoot -OutputPath $wxsPath
    $wxs = [IO.File]::ReadAllText($wxsPath)
    if ($wxs -notmatch "ComponentGroup Id=`"PayloadComponents`"") {
        throw "Generated WiX source is missing PayloadComponents."
    }
    if ($wxs -notmatch "Subdirectory=`"hooks`"") {
        throw "Generated WiX source is missing hook subdirectory files."
    }

    # The MSI custom actions call Register-Cursor.ps1; exercise that round trip.
    $installDirectory = Join-Path $root "install"
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $installDirectory "UiPilot.Cli.exe"), "")
    [IO.File]::WriteAllText((Join-Path $installDirectory "version.txt"), "0.1.0.99")
    $registerScript = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Register-Cursor.ps1"))
    $installedCommand = Join-Path $installDirectory "UiPilot.Cli.exe"
    $manifestPath = Join-Path $installDirectory "install-manifest.json"

    & $registerScript `
        -Action Register `
        -InstallDirectory $installDirectory `
        -McpConfigPath $mcpPath `
        -CursorSettingsPath $settingsPath

    $mcp = Read-UiPilotJson -Path $mcpPath
    Assert-Equal $installedCommand $mcp.mcpServers."uipilot-0.1.0.99".command "MSI registration must point at the installed CLI."
    Assert-Equal "other.exe" $mcp.mcpServers.other.command "MSI registration changed an unrelated MCP server."
    Assert-Equal $token $mcp.mcpServers."uipilot-0.1.0.99".env.UIPILOT_STATUS_TOKEN "MSI registration must preserve the status token."
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Registration must write install-manifest.json for uninstall."
    }
    $manifest = Read-UiPilotJson -Path $manifestPath
    Assert-Equal $mcpPath $manifest.mcpConfigPath "Manifest recorded the wrong MCP config path."
    Assert-Equal $settingsPath $manifest.cursorSettingsPath "Manifest recorded the wrong Cursor settings path."

    & $registerScript -Action Unregister -InstallDirectory $installDirectory

    $mcp = Read-UiPilotJson -Path $mcpPath
    if ($null -ne $mcp.mcpServers.PSObject.Properties["uipilot-0.1.0.99"]) {
        throw "Uninstall must remove Cursor's versioned UiPilot MCP entry."
    }
    Assert-Equal "other.exe" $mcp.mcpServers.other.command "Uninstall changed an unrelated MCP server."
    if (Test-Path -LiteralPath $manifestPath) {
        throw "Uninstall must delete install-manifest.json so the install directory can be removed."
    }
    $settings = Read-UiPilotJson -Path $settingsPath
    Assert-Equal 15 $settings."editor.fontSize" "Uninstall changed an unrelated Cursor setting."
    foreach ($name in @("uipilotStatus.host", "uipilotStatus.port", "uipilotStatus.token")) {
        if ($null -ne $settings.PSObject.Properties[$name]) {
            throw "Uninstall must remove extension setting '$name'."
        }
    }

    $packagesDirectory = Join-Path $root "packages"
    New-Item -ItemType Directory -Path $packagesDirectory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $packagesDirectory "UiPilot.Client.0.1.0.99.nupkg"), "nupkg")
    $nugetConfig = Join-Path $root "nuget.config"
    [IO.File]::WriteAllText(
        $nugetConfig,
        @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
  </packageSources>
</configuration>
"@)
    if (-not (Register-UiPilotNugetSource -PackagesDirectory $packagesDirectory -ConfigFile $nugetConfig)) {
        throw "Tester NuGet source registration failed."
    }
    $listed = & (Get-Command dotnet).Source nuget list source --configfile $nugetConfig
    $listedText = ($listed | Out-String)
    if ($listedText -notmatch "UiPilotInstalled") {
        throw "Registered NuGet source was not listed."
    }
    Unregister-UiPilotNugetSource -PackagesDirectory $packagesDirectory -ConfigFile $nugetConfig | Out-Null

    $skillSource = Join-Path $installDirectory "skills\uipilot-csharp-tests"
    New-Item -ItemType Directory -Path $skillSource -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $skillSource "SKILL.md"), "# test")
    if (-not (Install-UiPilotCursorSkill -InstallDirectory $installDirectory -HomeDirectory $root)) {
        throw "Tester skill installation failed."
    }
    $installedSkill = Join-Path $root ".cursor\skills\uipilot-csharp-tests\SKILL.md"
    if (-not (Test-Path -LiteralPath $installedSkill)) {
        throw "Tester skill was not copied into the Cursor skills directory."
    }
    Uninstall-UiPilotCursorSkill -HomeDirectory $root | Out-Null
    if (Test-Path -LiteralPath (Join-Path $root ".cursor\skills\uipilot-csharp-tests")) {
        throw "Tester skill uninstall must remove the copied skill."
    }

    Write-Host "Installer common tests passed."
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
