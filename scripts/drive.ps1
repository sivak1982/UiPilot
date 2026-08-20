<#
.SYNOPSIS
  Deprecated interactive driver. Use UiPilot MCP / CLI instead.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
throw @"
scripts/drive.ps1 is retired.
Use the Cursor UiPilot MCP tools (find_elements, click, ...) or UiPilot.Cli.
"@
