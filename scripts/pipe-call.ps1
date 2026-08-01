<#
.SYNOPSIS
  Send a single WpfPilot pipe request to a running app, bypassing the MCP CLI.

.DESCRIPTION
  Useful when the CLI binary is locked by a running MCP server but the in-app library
  already has a newer tool registered, or for quick protocol-level debugging.

.EXAMPLE
  .\pipe-call.ps1 -Method find_elements -Params @{ query = 'Dashboard'; limit = 20 }

.EXAMPLE
  .\pipe-call.ps1 -Method drag -Params @{ id = 'e14'; dx = 830; steps = 30 }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Method,
    [hashtable] $Params = @{},
    [int] $Pid_ = 0,
    [int] $TimeoutMs = 60000
)

$ErrorActionPreference = 'Stop'

$discoveryDir = Join-Path $env:TEMP 'wpfpilot'
$files = Get-ChildItem $discoveryDir -Filter *.json -ErrorAction SilentlyContinue
if ($Pid_ -gt 0) { $files = $files | Where-Object { $_.BaseName -eq "$Pid_" } }
if (-not $files) { throw "No WpfPilot app discovered in $discoveryDir." }
if ($files.Count -gt 1) { throw "Multiple apps running; pass -Pid_ to pick one." }

$info = Get-Content $files[0].FullName -Raw | ConvertFrom-Json

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream '.', $info.pipeName, 'InOut'
try {
    $pipe.Connect(5000)

    $encoding = New-Object System.Text.UTF8Encoding $false
    $reader = New-Object System.IO.StreamReader $pipe, $encoding, $false, 4096, $true
    $writer = New-Object System.IO.StreamWriter $pipe, $encoding, 4096, $true
    $writer.AutoFlush = $true
    $writer.NewLine = "`n"

    $request = @{
        jsonrpc = '2.0'
        id      = 1
        method  = $Method
        token   = $info.token
        params  = $Params
    } | ConvertTo-Json -Depth 10 -Compress

    $writer.WriteLine($request)

    $readTask = $reader.ReadLineAsync()
    if (-not $readTask.Wait($TimeoutMs)) { throw "Timed out after ${TimeoutMs}ms waiting for '$Method'." }

    $response = $readTask.Result
    if (-not $response) { throw 'Connection closed by the app.' }

    $parsed = $response | ConvertFrom-Json
    if ($parsed.PSObject.Properties.Name -contains 'error') {
        throw "$Method failed: $($parsed.error.message)"
    }

    $parsed.result | ConvertTo-Json -Depth 10
}
finally {
    $pipe.Dispose()
}
