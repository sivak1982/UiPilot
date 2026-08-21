Set-StrictMode -Version 2.0

$script:UiPilotRequiredRuntimeMajor = 8
$script:UiPilotRequiredSdkVersion = [Version]"8.0.400"
$script:UiPilotStatusPort = 17831
$script:UiPilotExtensionId = "uipilot.uipilot-status"
$script:UiPilotNugetSourceName = "UiPilotInstalled"
$script:UiPilotSkillName = "uipilot-csharp-tests"

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
    $compatible = @($versions | Where-Object { $_.Major -ge $script:UiPilotRequiredRuntimeMajor })
    if ($compatible.Count -eq 0) {
        throw @"
UiPilot.Cli requires the .NET $script:UiPilotRequiredRuntimeMajor or later Desktop-independent runtime (Microsoft.NETCore.App).
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
                $_ -ge $script:UiPilotRequiredSdkVersion
            }
    )
    if ($compatible.Count -eq 0) {
        throw @"
Building UiPilot requires .NET SDK $script:UiPilotRequiredSdkVersion or later.
Install it and run the build again:
  winget install Microsoft.DotNet.SDK.$script:UiPilotRequiredRuntimeMajor
  https://dotnet.microsoft.com/download/dotnet/$script:UiPilotRequiredRuntimeMajor.0
"@
    }

    return ($compatible | Sort-Object -Descending | Select-Object -First 1)
}

function ConvertFrom-UiPilotJsonc {
    param([Parameter(Mandatory = $true)][string]$Text)

    # Cursor settings are JSONC: // and /* */ comments plus trailing commas are legal.
    $withoutBlock = [regex]::Replace($Text, '/\*[\s\S]*?\*/', '')
    $lines = foreach ($line in ($withoutBlock -split "`r?`n")) {
        $stripped = [regex]::Replace($line, '(?<!:)//.*$', '')
        $stripped
    }
    $joined = [string]::Join("`n", $lines)
    $joined = [regex]::Replace($joined, ',(\s*[}\]])', '$1')
    return $joined
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
        $json = ConvertFrom-UiPilotJsonc -Text $content
        return ($json | ConvertFrom-Json)
    }
    catch {
        throw "Cannot update '$Path' because it does not contain valid JSON/JSONC: $($_.Exception.Message)"
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
        [Parameter(Mandatory = $true)][string]$Version,
        [int]$StatusPort = $script:UiPilotStatusPort
    )

    $config = Read-UiPilotJson -Path $ConfigPath
    $mcpServersProperty = $config.PSObject.Properties["mcpServers"]
    if ($null -eq $mcpServersProperty -or $null -eq $mcpServersProperty.Value) {
        $config | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([pscustomobject]@{}) -Force
    }

    $serverName = Get-UiPilotMcpServerName -Version $Version
    $serverEnvironment = [pscustomobject]@{}
    $existingServerProperty = @(
        $config.mcpServers.PSObject.Properties |
            Where-Object { $_.Name -eq $serverName -or $_.Name -eq "uipilot" -or $_.Name.StartsWith("uipilot-") }
    ) | Select-Object -First 1
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
    foreach ($property in @($config.mcpServers.PSObject.Properties)) {
        if (($property.Name -eq "uipilot" -or $property.Name.StartsWith("uipilot-")) -and
            $property.Name -ne $serverName) {
            # UiPilot owns this namespace. Remove entries from older install directories as well
            # so Cursor cannot keep launching stale binaries with stale status tokens.
            $config.mcpServers.PSObject.Properties.Remove($property.Name)
        }
    }
    $config.mcpServers | Add-Member -MemberType NoteProperty -Name $serverName -Value $server -Force
    Write-UiPilotJson -Path $ConfigPath -Value $config
}

function Get-UiPilotMcpServerName {
    param([Parameter(Mandatory = $true)][string]$Version)

    if ($Version -notmatch "^\d+\.\d+\.\d+\.\d+$") {
        throw "UiPilot MCP version '$Version' must use major.minor.patch.build format."
    }
    return "uipilot-$Version"
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

    $existing = Get-UiPilotStatusToken -ConfigPath $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($existing)) {
        return $existing
    }

    return New-UiPilotStatusToken
}

function Get-UiPilotStatusToken {
    param([Parameter(Mandatory = $true)][string]$ConfigPath)

    $config = Read-UiPilotJson -Path $ConfigPath
    $server = $null
    if ($null -ne $config.PSObject.Properties["mcpServers"] -and
        $null -ne $config.mcpServers) {
        $server = @(
            $config.mcpServers.PSObject.Properties |
                Where-Object { $_.Name -eq "uipilot" -or $_.Name.StartsWith("uipilot-") } |
                ForEach-Object { $_.Value }
        ) | Select-Object -Last 1
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

    return $null
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

function Remove-UiPilotExtensionSettings {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsPath,
        [string]$ExpectedToken = ""
    )

    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return $false
    }
    $settings = Read-UiPilotJson -Path $SettingsPath
    $tokenProperty = $settings.PSObject.Properties["uipilotStatus.token"]
    if ($null -eq $tokenProperty) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedToken) -and
        -not [string]::Equals([string]$tokenProperty.Value, $ExpectedToken, [StringComparison]::Ordinal)) {
        Write-Warning "Cursor's UiPilot status token was changed after install; extension settings were left unchanged."
        return $false
    }

    foreach ($name in @("uipilotStatus.host", "uipilotStatus.port", "uipilotStatus.token")) {
        $settings.PSObject.Properties.Remove($name)
    }
    Write-UiPilotJson -Path $SettingsPath -Value $settings
    return $true
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

    $removed = $false
    foreach ($property in @($config.mcpServers.PSObject.Properties)) {
        if ($property.Name -ne "uipilot" -and -not $property.Name.StartsWith("uipilot-")) {
            continue
        }

        $configuredCommand = [string]$property.Value.command
        if ([string]::IsNullOrWhiteSpace($configuredCommand) -or
            -not [string]::Equals(
                [IO.Path]::GetFullPath($configuredCommand),
                [IO.Path]::GetFullPath($InstalledCommandPath),
                [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $config.mcpServers.PSObject.Properties.Remove($property.Name)
        $removed = $true
    }

    if (-not $removed) {
        Write-Warning "Cursor's UiPilot MCP entries point elsewhere; they were left unchanged."
        return $false
    }

    Write-UiPilotJson -Path $ConfigPath -Value $config
    return $true
}

function Get-UiPilotManifestPath {
    param([Parameter(Mandatory = $true)][string]$InstallDirectory)

    return (Join-Path $InstallDirectory "install-manifest.json")
}

function Get-UiPilotInstalledCommandPath {
    param([Parameter(Mandatory = $true)][string]$InstallDirectory)

    $windowsCommand = Join-Path $InstallDirectory "UiPilot.Cli.exe"
    if (Test-Path -LiteralPath $windowsCommand -PathType Leaf) {
        return $windowsCommand
    }

    return (Join-Path $InstallDirectory "UiPilot.Cli")
}

function Get-UiPilotInstalledVersion {
    param([Parameter(Mandatory = $true)][string]$InstallDirectory)

    $versionPath = Join-Path $InstallDirectory "version.txt"
    if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        throw "UiPilot version metadata was not found at '$versionPath'."
    }

    $version = [IO.File]::ReadAllText($versionPath).Trim()
    $null = Get-UiPilotMcpServerName -Version $version
    return $version
}

function Install-UiPilotCursorExtension {
    param([Parameter(Mandatory = $true)][string]$VsixPath)

    if (-not (Test-Path -LiteralPath $VsixPath -PathType Leaf)) {
        Write-Warning "Cursor extension VSIX was not found at '$VsixPath'."
        return
    }

    $cursorCommand = Get-UiPilotCursorCommand
    if ($cursorCommand) {
        & $cursorCommand --install-extension $VsixPath --force
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Installed the UiPilot Status extension in Cursor."
            return
        }

        Write-Warning "Cursor CLI could not install the extension. In Cursor, use 'Extensions: Install from VSIX' and select '$VsixPath'."
        return
    }

    Write-Warning "Cursor CLI was not found. In Cursor, use 'Extensions: Install from VSIX' and select '$VsixPath'."
}

function Uninstall-UiPilotCursorExtension {
    $cursorCommand = Get-UiPilotCursorCommand
    if (-not $cursorCommand) {
        Write-Warning "Cursor CLI was not found; remove extension '$script:UiPilotExtensionId' manually."
        return $false
    }

    & $cursorCommand --uninstall-extension $script:UiPilotExtensionId
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Cursor CLI could not remove extension '$script:UiPilotExtensionId'."
        return $false
    }
    Write-Host "Removed the UiPilot Status extension from Cursor."
    return $true
}

function Get-UiPilotCursorCommand {
    $cursor = Get-Command cursor -ErrorAction SilentlyContinue
    if ($cursor) {
        return $cursor.Source
    }

    # Windows Installer custom actions do not always inherit the interactive user's PATH.
    # Cursor's per-user install puts its CLI here by default.
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $installedCursor = Join-Path $env:LOCALAPPDATA "Programs\cursor\resources\app\bin\cursor.cmd"
        if (Test-Path -LiteralPath $installedCursor -PathType Leaf) {
            return $installedCursor
        }
    }

    return $null
}

function Get-UiPilotPackagesDirectory {
    param([Parameter(Mandatory = $true)][string]$InstallDirectory)

    return (Join-Path $InstallDirectory "packages")
}

function Get-UiPilotSkillSourceDirectory {
    param([Parameter(Mandatory = $true)][string]$InstallDirectory)

    return (Join-Path $InstallDirectory "skills\$script:UiPilotSkillName")
}

function Get-UiPilotSkillInstallDirectory {
    param([string]$HomeDirectory = "")

    if ([string]::IsNullOrWhiteSpace($HomeDirectory)) {
        $HomeDirectory = $HOME
    }
    if ([string]::IsNullOrWhiteSpace($HomeDirectory)) {
        return $null
    }
    return (Join-Path $HomeDirectory ".cursor\skills\$script:UiPilotSkillName")
}

function Register-UiPilotNugetSource {
    param(
        [Parameter(Mandatory = $true)][string]$PackagesDirectory,
        [string]$ConfigFile = ""
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Warning "dotnet was not found; testers must add the UiPilot package source manually: $PackagesDirectory"
        return $false
    }

    if (-not (Test-Path -LiteralPath $PackagesDirectory)) {
        Write-Warning "UiPilot tester packages were not found at '$PackagesDirectory'."
        return $false
    }

    $packages = @(Get-ChildItem -LiteralPath $PackagesDirectory -Filter *.nupkg -ErrorAction SilentlyContinue)
    if ($packages.Count -eq 0) {
        Write-Warning "UiPilot tester packages were not found at '$PackagesDirectory'."
        return $false
    }

    $arguments = @("nuget", "remove", "source", $script:UiPilotNugetSourceName)
    if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
        $arguments += @("--configfile", $ConfigFile)
    }
    & $dotnet.Source @arguments 2>$null | Out-Null

    $arguments = @(
        "nuget", "add", "source", $PackagesDirectory,
        "--name", $script:UiPilotNugetSourceName
    )
    if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
        $arguments += @("--configfile", $ConfigFile)
    }
    & $dotnet.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not register the UiPilot NuGet source at '$PackagesDirectory'."
        return $false
    }

    Write-Host "Registered NuGet source '$script:UiPilotNugetSourceName' -> $PackagesDirectory"
    return $true
}

function Unregister-UiPilotNugetSource {
    param(
        [Parameter(Mandatory = $true)][string]$PackagesDirectory,
        [string]$ConfigFile = ""
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        return $false
    }

    $listArguments = @("nuget", "list", "source")
    if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
        $listArguments += @("--configfile", $ConfigFile)
    }
    $listed = & $dotnet.Source @listArguments 2>$null
    $expected = [IO.Path]::GetFullPath($PackagesDirectory).TrimEnd('\', '/')
    $pointsHere = $false
    foreach ($line in @($listed)) {
        if ($line -and $line.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $pointsHere = $true
            break
        }
    }
    if (-not $pointsHere) {
        return $false
    }

    $arguments = @("nuget", "remove", "source", $script:UiPilotNugetSourceName)
    if (-not [string]::IsNullOrWhiteSpace($ConfigFile)) {
        $arguments += @("--configfile", $ConfigFile)
    }
    & $dotnet.Source @arguments 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Install-UiPilotCursorSkill {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [string]$HomeDirectory = ""
    )

    $source = Get-UiPilotSkillSourceDirectory -InstallDirectory $InstallDirectory
    $skillFile = Join-Path $source "SKILL.md"
    if (-not (Test-Path -LiteralPath $skillFile -PathType Leaf)) {
        Write-Warning "UiPilot tester skill was not found at '$skillFile'."
        return $false
    }

    $destination = Get-UiPilotSkillInstallDirectory -HomeDirectory $HomeDirectory
    if ([string]::IsNullOrWhiteSpace($destination)) {
        Write-Warning "The current user's home directory is unavailable; the tester skill was not installed."
        return $false
    }

    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $skillFile -Destination (Join-Path $destination "SKILL.md") -Force
    Write-Host "Installed the UiPilot tester skill at $destination"
    return $true
}

function Uninstall-UiPilotCursorSkill {
    param([string]$HomeDirectory = "")

    $destination = Get-UiPilotSkillInstallDirectory -HomeDirectory $HomeDirectory
    if ([string]::IsNullOrWhiteSpace($destination) -or -not (Test-Path -LiteralPath $destination)) {
        return $false
    }
    Remove-Item -LiteralPath $destination -Recurse -Force
    return $true
}

function Register-UiPilotCursorIntegration {
    param(
        [Parameter(Mandatory = $true)][string]$InstallDirectory,
        [string]$McpConfigPath = "",
        [string]$CursorSettingsPath = ""
    )

    if ([string]::IsNullOrWhiteSpace($McpConfigPath)) {
        if ([string]::IsNullOrWhiteSpace($HOME)) {
            throw "The current user's home directory is unavailable; specify -McpConfigPath explicitly."
        }
        $McpConfigPath = Join-Path $HOME ".cursor\mcp.json"
    }

    if ([string]::IsNullOrWhiteSpace($CursorSettingsPath)) {
        if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
            throw "APPDATA is not defined; specify -CursorSettingsPath explicitly."
        }
        $CursorSettingsPath = Join-Path $env:APPDATA "Cursor\User\settings.json"
    }

    $commandPath = Get-UiPilotInstalledCommandPath -InstallDirectory $InstallDirectory
    $version = Get-UiPilotInstalledVersion -InstallDirectory $InstallDirectory
    $statusToken = Get-OrCreateUiPilotStatusToken -ConfigPath $McpConfigPath
    Set-UiPilotMcpServer `
        -ConfigPath $McpConfigPath `
        -CommandPath $commandPath `
        -StatusToken $statusToken `
        -Version $version
    Set-UiPilotExtensionSettings -SettingsPath $CursorSettingsPath -StatusToken $statusToken

    # Uninstall reads this back so it can clean up non-default Cursor config locations.
    $manifest = [pscustomobject]@{
        installedAtUtc = [DateTime]::UtcNow.ToString("o")
        installDirectory = $InstallDirectory
        mcpConfigPath = $McpConfigPath
        cursorSettingsPath = $CursorSettingsPath
        command = $commandPath
        mcpServerName = Get-UiPilotMcpServerName -Version $version
        version = $version
        requiredRuntime = "$script:UiPilotRequiredRuntimeMajor.0"
    }
    [IO.File]::WriteAllText(
        (Get-UiPilotManifestPath -InstallDirectory $InstallDirectory),
        ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))

    Install-UiPilotCursorExtension -VsixPath (Join-Path $InstallDirectory "UiPilot.Status.vsix")
    Register-UiPilotNugetSource -PackagesDirectory (Get-UiPilotPackagesDirectory -InstallDirectory $InstallDirectory) | Out-Null
    Install-UiPilotCursorSkill -InstallDirectory $InstallDirectory | Out-Null
}

function Write-UiPilotWixPayloadComponents {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadDirectory,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $payloadRoot = [IO.Path]::GetFullPath($PayloadDirectory).TrimEnd('\', '/')
    $files = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Cannot harvest an empty payload directory: $payloadRoot"
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <ComponentGroup Id="PayloadComponents" Directory="INSTALLFOLDER">')
    $index = 0
    foreach ($file in $files) {
        $index++
        $relative = $file.FullName.Substring($payloadRoot.Length).TrimStart('\', '/')
        $directory = [IO.Path]::GetDirectoryName($relative)
        $source = [Security.SecurityElement]::Escape($file.FullName)
        $componentDir = ""
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            $componentDir = " Subdirectory=`"$([Security.SecurityElement]::Escape($directory))`""
        }
        [void]$sb.AppendLine("      <Component Id=`"cmp$index`" Guid=`"*`"$componentDir>")
        [void]$sb.AppendLine("        <File Id=`"fil$index`" Source=`"$source`" KeyPath=`"yes`" />")
        [void]$sb.AppendLine("      </Component>")
    }
    [void]$sb.AppendLine('    </ComponentGroup>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('</Wix>')
    [IO.File]::WriteAllText($OutputPath, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
}
