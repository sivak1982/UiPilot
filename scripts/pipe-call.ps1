<#
.SYNOPSIS
  Deprecated: pre-MCP pipe helpers. Use the UiPilot CLI / MCP server instead.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Method,
    [hashtable] $Params = @{},
    [int] $Pid_ = 0,
    [int] $TimeoutMs = 60000
)

$ErrorActionPreference = 'Stop'
throw @"
scripts/pipe-call.ps1 is retired (pre-MCP protocol).
Use the UiPilot MCP server or UiPilot.Cli instead of raw pipe JSON-RPC.
Requested method was '$Method'.
"@
