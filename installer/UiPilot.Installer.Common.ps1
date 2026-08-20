Set-StrictMode -Version 2.0

$script:UiPilotRequiredRuntimeMajor = 10
$script:UiPilotRequiredSdkVersion = [Version]"10.0.301"
$script:UiPilotStatusPort = 17831
$script:UiPilotExtensionId = "uipilot.uipilot-status"

function Assert-UiPilotWindows {
    if ($env:OS -ne "Windows_NT") {
        throw "UiPilot's installer supports Windows only."
    }

    if ($PSVersionTable.PSVersion -lt [Version]"5.1") {
        throw "UiPilot requires Windows PowerShell 5.1 or PowerShell 7 or later."
    }
}

function Get-UiPilotDotNetVersions {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("runtime", "sdk")]
        [string]$Kind
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return @()
    }

    $arguments = if ($Kind -eq "runtime") { "--list-runtimes" } else { "--list-sdks" }
    $lines = & $dotnet.Source $arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    $versions = @()
    foreach ($line in $lines) {
        if ($Kind -eq "runtime") {
            if ($line -match "^Microsoft\.NETCore\.App\s+(\d+\.\d+\.\d+)") {
                $versions += [Version]$Matches[1]
            }
        }
        elseif ($line -match "^(\d+\.\d+\.\d+)") {
            $versions += [Version]$Matches[1]
        }
    }

    return $versions
}

function Assert-UiPilotRuntime {
    $versions = @(Get-UiPilotDotNetVersions -Kind runtime)
    $compatible = @($versions | Where-Object { $_.Major -eq $script:UiPilotRequiredRuntimeMajor })
    if ($compatible.Count -eq 0) {
        throw @"
UiPilot.Cli requires the .NET $script:UiPilotRequiredRuntimeMajor Desktop-independent runtime (Microsoft.NETCore.App).
Install it and run this installer again:
  winget install Microsoft.DotNet.Runtime.$script:UiPilotRequiredRuntimeMajor
  https://dotnet.microsoft.com/download/dotnet/$script:UiPilotRequiredRuntimeMajor.0
"@
    }

    return ($compatible | Sort-Object -Descending | Select-Object -First 1)
}

function Assert-UiPilotBuildSdk {
    $versions = @(Get-UiPilotDotNetVersions -Kind sdk)
    $compatible = @(
        $versions |
            Where-Object {
                $_.Major -eq $script:UiPilotRequiredSdkVersion.Major -and
                $_ -ge $script:UiPilotRequiredSdkVersion
            }
    )
    if ($compatible.Count -eq 0) {
        throw @"
Building UiPilot requires .NET SDK $script:UiPilotRequiredSdkVersion or a newer .NET 10 SDK.
Install it and run the build again:
  winget install Microsoft.DotNet.SDK.10
  https://dotnet.microsoft.com/download/dotnet/10.0
"@
    }

    return ($compatible | Sort-Object -Descending | Select-Object -First 1)
}

function Read-UiPilotJson {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{}
    }

    $content = [IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($content)) {
        return [pscustomobject]@{}
    }

    try {
        return ($content | ConvertFrom-Json)
    }
    catch {
        throw "Cannot update '$Path' because it does not contain valid JSON: $($_.Exception.Message)"
    }
}

function Write-UiPilotJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    if (Test-Path -LiteralPath $Path) {
        $backup = "$Path.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $Path -Destination $backup -Force
        Write-Host "Backed up existing JSON configuration to $backup"
    }

    $json = $Value | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

function Set-UiPilotMcpServer {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$CommandPath,
        [Parameter(Mandatory = $true)][string]$StatusToken,
        [int]$StatusPort = $script:UiPilotStatusPort
    )

    $config = Read-UiPilotJson -Path $ConfigPath
    $mcpServersProperty = $config.PSObject.Properties["mcpServers"]
    if ($null -eq $mcpServersProperty -or $null -eq $mcpServersProperty.Value) {
        $config | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([pscustomobject]@{}) -Force
    }

    $serverEnvironment = [pscustomobject]@{}
    $existingServerProperty = $config.mcpServers.PSObject.Properties["uipilot"]
    if ($null -ne $existingServerProperty -and
        $null -ne $existingServerProperty.Value -and
        $null -ne $existingServerProperty.Value.PSObject.Properties["env"] -and
        $null -ne $existingServerProperty.Value.env) {
        foreach ($property in $existingServerProperty.Value.env.PSObject.Properties) {
            $serverEnvironment | Add-Member `
                -MemberType NoteProperty `
                -Name $property.Name `
                -Value $property.Value `
                -Force
        }
    }
    $serverEnvironment | Add-Member -MemberType NoteProperty -Name "UIPILOT_STATUS_PORT" -Value ([string]$StatusPort) -Force
    $serverEnvironment | Add-Member -MemberType NoteProperty -Name "UIPILOT_STATUS_TOKEN" -Value $StatusToken -Force

    $server = [pscustomobject]@{
        command = $CommandPath
        args = @()
        env = $serverEnvironment
    }
    $config.mcpServers | Add-Member -MemberType NoteProperty -Name "uipilot" -Value $server -Force
    Write-UiPilotJson -Path $ConfigPath -Value $config
}

function New-UiPilotStatusToken {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }
    return (($bytes | ForEach-Object { $_.ToString("x2") }) -join "")
}

function Get-OrCreateUiPilotStatusToken {
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    $config = Read-UiPilotJson -Path $ConfigPath
    $server = $null
    if ($null -ne $config.PSObject.Properties["mcpServers"] -and
        $null -ne $config.mcpServers -and
        $null -ne $config.mcpServers.PSObject.Properties["uipilot"]) {
        $server = $config.mcpServers.uipilot
    }

    if ($null -ne $server -and
        $null -ne $server.PSObject.Properties["env"] -and
        $null -ne $server.env -and
        $null -ne $server.env.PSObject.Properties["UIPILOT_STATUS_TOKEN"]) {
        $existing = [string]$server.env.UIPILOT_STATUS_TOKEN
        if (-not [string]::IsNullOrWhiteSpace($existing)) {
            return $existing
        }
    }

    return New-UiPilotStatusToken
}

function Set-UiPilotExtensionSettings {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [Parameter(Mandatory = $true)][string]$StatusToken,
        [int]$StatusPort = $script:UiPilotStatusPort
    )

    $settings = Read-UiPilotJson -Path $SettingsPath
    $settings | Add-Member -MemberType NoteProperty -Name "uipilotStatus.host" -Value "127.0.0.1" -Force
    $settings | Add-Member -MemberType NoteProperty -Name "uipilotStatus.port" -Value $StatusPort -Force
    $settings | Add-Member -MemberType NoteProperty -Name "uipilotStatus.token" -Value $StatusToken -Force
    Write-UiPilotJson -Path $SettingsPath -Value $settings
}

function Remove-UiPilotMcpServer {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigPath,
        [Parameter(Mandatory = $true)][string]$InstalledCommandPath
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        return $false
    }

    $config = Read-UiPilotJson -Path $ConfigPath
    $mcpServersProperty = $config.PSObject.Properties["mcpServers"]
    if ($null -eq $mcpServersProperty -or $null -eq $mcpServersProperty.Value) {
        return $false
    }

    $property = $config.mcpServers.PSObject.Properties["uipilot"]
    if ($null -eq $property) {
        return $false
    }

    $configuredCommand = $property.Value.command
    if (-not [string]::Equals(
        [IO.Path]::GetFullPath([string]$configuredCommand),
        [IO.Path]::GetFullPath($InstalledCommandPath),
        [StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "Cursor's 'uipilot' MCP entry points elsewhere; it was left unchanged."
        return $false
    }

    $config.mcpServers.PSObject.Properties.Remove("uipilot")
    Write-UiPilotJson -Path $ConfigPath -Value $config
    return $true
}
