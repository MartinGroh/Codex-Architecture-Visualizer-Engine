[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PluginPath,
    [switch] $RequireBinaries
)

$ErrorActionPreference = 'Stop'
# Validate a selected package independently of checkout/version files. Repository checks are
# additionally owned by validate-plugin-metadata.ps1; both consume the same package contract.
Import-Module (Join-Path $PSScriptRoot 'plugin-tooling.psm1') -Force
Test-CavePluginPackage -PluginPath $PluginPath -RequireBinaries:$RequireBinaries -SkipRepositoryChecks | Out-Null
Write-Host "Plugin package validation passed: $PluginPath" -ForegroundColor Green
