<#
    End-to-end smoke test for the UiPilot named-pipe surface.

    Launches the sample app, waits for its discovery file, connects to the pipe, and exercises a
    representative set of tools directly (bypassing MCP, so it is easy to run from PowerShell).
#>
param(
    [string]$Exe = "$PSScriptRoot\..\samples\SampleApp\bin\Debug\net8.0-windows\SampleApp.exe"
)

$ErrorActionPreference = "Stop"

function Send-Rpc($writer, $reader, $method, $token, $params) {
    $req = @{ jsonrpc = "2.0"; id = 1; method = $method; token = $token; params = $params } |
        ConvertTo-Json -Compress -Depth 10
    $writer.WriteLine($req)
    $line = $reader.ReadLine()
    if (-not $line) { throw "No response to '$method'." }
    $resp = $line | ConvertFrom-Json
    if ($resp.PSObject.Properties.Name -contains 'error') {
        throw "RPC '$method' failed: $($resp.error.message)"
    }
    return $resp.result
}

if (-not (Test-Path $Exe)) { throw "Sample app not built: $Exe" }

$env:UIPILOT_ENABLE = "1"
$proc = Start-Process -FilePath $Exe -PassThru
Write-Host "Started SampleApp pid=$($proc.Id)"

try {
    $file = Join-Path (Join-Path $env:TEMP "uipilot") "$($proc.Id).json"
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $file) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path $file)) { throw "Discovery file never appeared: $file" }

    $info = Get-Content $file -Raw | ConvertFrom-Json
    Write-Host "Discovery: pipe=$($info.pipeName) protocol=$($info.protocolVersion)"

    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", $info.pipeName, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    $pipe.Connect(5000)
    $enc = New-Object System.Text.UTF8Encoding($false)
    $reader = New-Object System.IO.StreamReader($pipe, $enc)
    $writer = New-Object System.IO.StreamWriter($pipe, $enc)
    $writer.NewLine = "`n"
    $writer.AutoFlush = $true

    $token = $info.token

    $ping = Send-Rpc $writer $reader "ping" $token @{}
    Write-Host "ping -> pong=$($ping.pong)"

    $desc = Send-Rpc $writer $reader "describe" $token @{}
    Write-Host "describe -> $($desc.tools.Count) tools"

    $windows = Send-Rpc $writer $reader "list_windows" $token @{}
    Write-Host "list_windows -> $($windows.windows.Count) window(s): '$($windows.windows[0].text)'"

    $found = Send-Rpc $writer $reader "find_elements" $token @{ query = "Greet"; limit = 50 }
    $foundTotal = if ($null -ne $found.total) { $found.total } else { $found.count }
    Write-Host "find_elements('Greet') -> $foundTotal match(es)"
    $button = $found.elements | Where-Object { $_.automationId -eq "GreetButton" } | Select-Object -First 1
    if (-not $button) { throw "GreetButton not found." }
    Write-Host "  GreetButton id=$($button.id) enabled=$($button.enabled) bounds=$($button.width)x$($button.height)"

    $click = Send-Rpc $writer $reader "click" $token @{ id = $button.id }
    Write-Host "click -> method=$($click.method)"

    $greeting = Send-Rpc $writer $reader "find_elements" $token @{ query = "Hello" }
    $greetingTotal = if ($null -ne $greeting.total) { $greeting.total } else { $greeting.count }
    Write-Host "after click, elements containing 'Hello': $greetingTotal"

    $bind = Send-Rpc $writer $reader "get_binding_errors" $token @{}
    Write-Host "get_binding_errors -> $($bind.count) error(s)"
    if ($bind.count -lt 1) { throw "Expected the deliberate MissingProperty binding error." }
    Write-Host "  first: $($bind.errors[0].Substring(0, [Math]::Min(90, $bind.errors[0].Length)))..."

    $layout = Send-Rpc $writer $reader "analyze_layout" $token @{}
    Write-Host "analyze_layout -> $($layout.count) issue(s)"

    $shot = Send-Rpc $writer $reader "screenshot" $token @{}
    Write-Host "screenshot -> $($shot.width)x$($shot.height) base64Len=$($shot.base64.Length)"
    if ($shot.base64.Length -lt 100) { throw "Screenshot looks empty." }

    Write-Host "`nSMOKE TEST PASSED" -ForegroundColor Green
}
finally {
    if ($writer) { $writer.Dispose() }
    if ($reader) { $reader.Dispose() }
    if ($pipe) { $pipe.Dispose() }
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
