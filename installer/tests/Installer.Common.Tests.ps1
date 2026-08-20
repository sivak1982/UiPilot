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

    Set-UiPilotMcpServer `
        -ConfigPath $mcpPath `
        -CommandPath "C:\UiPilot\UiPilot.Cli.exe" `
        -StatusToken $token
    Set-UiPilotExtensionSettings `
        -SettingsPath $settingsPath `
        -StatusToken $token

    $mcp = Read-UiPilotJson -Path $mcpPath
    Assert-Equal "other.exe" $mcp.mcpServers.other.command "Unrelated MCP server was changed."
    Assert-Equal $true $mcp.unrelated "Unrelated MCP setting was changed."
    Assert-Equal $token $mcp.mcpServers.uipilot.env.UIPILOT_STATUS_TOKEN "MCP token mismatch."
    Assert-Equal "17831" $mcp.mcpServers.uipilot.env.UIPILOT_STATUS_PORT "MCP port mismatch."

    $mcp.mcpServers.uipilot.env | Add-Member -MemberType NoteProperty -Name "CUSTOM_ENV" -Value "keep-me" -Force
    Write-UiPilotJson -Path $mcpPath -Value $mcp
    Set-UiPilotMcpServer `
        -ConfigPath $mcpPath `
        -CommandPath "C:\UiPilot\UiPilot.Cli.exe" `
        -StatusToken $token
    $mcp = Read-UiPilotJson -Path $mcpPath
    Assert-Equal "keep-me" $mcp.mcpServers.uipilot.env.CUSTOM_ENV "Unrelated MCP environment was changed."

    $settings = Read-UiPilotJson -Path $settingsPath
    Assert-Equal 15 $settings."editor.fontSize" "Unrelated Cursor setting was changed."
    Assert-Equal "127.0.0.1" $settings."uipilotStatus.host" "Extension host mismatch."
    Assert-Equal 17831 $settings."uipilotStatus.port" "Extension port mismatch."
    Assert-Equal $token $settings."uipilotStatus.token" "Extension token mismatch."

    $preserved = Get-OrCreateUiPilotStatusToken -ConfigPath $mcpPath
    Assert-Equal $token $preserved "Reinstall must preserve the existing status token."

    Write-Host "Installer common tests passed."
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
