<#
    Ad-hoc WpfPilot driver: connects to a running WpfPilot app over its named pipe and runs a
    small sequence of operations (find / click-by-text / screenshot / binding+layout diagnostics).

    Connects fresh each invocation (reading the discovery file), so the target app stays alive
    across calls. Screenshots are decoded from base64 and written to -ShotPath as PNG.

    Examples:
      drive.ps1 -Windows
      drive.ps1 -ClickText 'Send Message' -ShotPath out\send.png
      drive.ps1 -ClickText 'C# Scripting','Editor' -ShotPath out\editor.png
      drive.ps1 -FindQuery Button
      drive.ps1 -Bindings -Layout
#>
param(
    [int]$TargetPid = 0,
    [string[]]$ClickText,
    [string]$ShotPath,
    [string]$ShotId,
    [string]$FindQuery,
    [int]$FindLimit = 60,
    [switch]$Bindings,
    [switch]$Layout,
    [switch]$Windows,
    [switch]$ClearBindings,
    [string]$WinState,
    [switch]$Front
)

$ErrorActionPreference = "Stop"

function Send-Rpc($writer, $reader, $method, $token, $params) {
    $req = @{ jsonrpc = "2.0"; id = 1; method = $method; token = $token; params = $params } |
        ConvertTo-Json -Compress -Depth 12
    $writer.WriteLine($req)
    $line = $reader.ReadLine()
    if (-not $line) { throw "No response to '$method'." }
    $resp = $line | ConvertFrom-Json
    if ($resp.PSObject.Properties.Name -contains 'error') {
        throw "RPC '$method' failed: $($resp.error.message)"
    }
    return $resp.result
}

# --- locate discovery file ---
$discDir = Join-Path $env:TEMP "wpfpilot"
if (-not (Test-Path $discDir)) { throw "No discovery dir: $discDir (is the app running?)" }
$files = Get-ChildItem $discDir -Filter *.json
if ($TargetPid -ne 0) { $files = $files | Where-Object { $_.BaseName -eq "$TargetPid" } }
# keep only live processes
$live = @()
foreach ($f in $files) {
    try { $null = Get-Process -Id ([int]$f.BaseName) -ErrorAction Stop; $live += $f } catch {}
}
if ($live.Count -eq 0) { throw "No live WpfPilot app found (pid filter=$TargetPid)." }
if ($live.Count -gt 1) { throw "Multiple apps live: $($live.BaseName -join ', '). Pass -TargetPid." }
$info = Get-Content $live[0].FullName -Raw | ConvertFrom-Json

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $info.pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
$pipe.Connect(5000)
$enc = New-Object System.Text.UTF8Encoding($false)
$reader = New-Object System.IO.StreamReader($pipe, $enc)
$writer = New-Object System.IO.StreamWriter($pipe, $enc)
$writer.NewLine = "`n"
$writer.AutoFlush = $true
$token = $info.token

try {
    if ($Windows) {
        $w = Send-Rpc $writer $reader "list_windows" $token @{}
        $w.windows | ForEach-Object { "WINDOW id=$($_.id) type=$($_.type) text='$($_.text)' $([int]$_.width)x$([int]$_.height)" }
    }

    if ($Front) {
        $r = Send-Rpc $writer $reader "bring_to_front" $token @{}
        "FRONT -> state=$($r.state)"
    }

    if ($WinState) {
        $r = Send-Rpc $writer $reader "set_window_state" $token @{ state = $WinState }
        "WINSTATE -> $($r.state)"
    }

    foreach ($text in $ClickText) {
        $found = Send-Rpc $writer $reader "find_elements" $token @{ query = $text; limit = 80 }
        # prefer an exact MenuItem/Button header match
        $target = $found.elements | Where-Object { $_.text -eq $text } | Select-Object -First 1
        if (-not $target) { $target = $found.elements | Where-Object { $_.type -like '*MenuItem*' } | Select-Object -First 1 }
        if (-not $target) { $target = $found.elements | Select-Object -First 1 }
        if (-not $target) { throw "No element found for click text '$text'." }
        $click = Send-Rpc $writer $reader "click" $token @{ id = $target.id }
        "CLICK '$text' -> id=$($target.id) type=$($target.type) method=$($click.method)"
        Start-Sleep -Milliseconds 500
    }

    if ($FindQuery) {
        $found = Send-Rpc $writer $reader "find_elements" $token @{ query = $FindQuery; limit = $FindLimit }
        "FIND '$FindQuery' -> $($found.count) match(es)"
        $found.elements | ForEach-Object {
            "  id=$($_.id) type=$($_.type) name='$($_.name)' text='$($_.text)' vis=$($_.visible) en=$($_.enabled) $([int]$_.width)x$([int]$_.height)"
        }
    }

    if ($ShotPath) {
        Start-Sleep -Milliseconds 400
        $shotParams = @{}
        if ($ShotId) { $shotParams = @{ id = $ShotId } }
        $shot = Send-Rpc $writer $reader "screenshot" $token $shotParams
        $dir = Split-Path $ShotPath -Parent
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [IO.File]::WriteAllBytes($ShotPath, [Convert]::FromBase64String($shot.base64))
        "SHOT -> $ShotPath ($($shot.width)x$($shot.height))"
    }

    if ($Layout) {
        $l = Send-Rpc $writer $reader "analyze_layout" $token @{}
        "LAYOUT issues: $($l.count)"
        $l.issues | Select-Object -First 40 | ForEach-Object { "  $($_ | ConvertTo-Json -Compress -Depth 5)" }
    }

    if ($Bindings) {
        $b = Send-Rpc $writer $reader "get_binding_errors" $token @{ clear = [bool]$ClearBindings }
        "BINDING errors: $($b.count)"
        $b.errors | Select-Object -First 40 | ForEach-Object { "  $_" }
    }
}
finally {
    if ($writer) { $writer.Dispose() }
    if ($reader) { $reader.Dispose() }
    if ($pipe) { $pipe.Dispose() }
}
