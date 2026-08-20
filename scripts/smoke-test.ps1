<#
.SYNOPSIS
  Deprecated smoke test. Use `dotnet test` instead.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
throw @"
scripts/smoke-test.ps1 is retired (pre-MCP protocol).
Run: dotnet test UiPilot.sln
"@
