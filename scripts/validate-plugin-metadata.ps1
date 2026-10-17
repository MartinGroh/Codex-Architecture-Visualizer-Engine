[CmdletBinding()]
param(
    [string] $PluginPath,
    [switch] $RequireBinaries,
    [switch] $SkipRepositoryChecks
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PluginPath)) { $PluginPath = Join-Path $repoRoot 'plugins/cave' }
Import-Module (Join-Path $PSScriptRoot 'plugin-tooling.psm1') -Force
Test-CavePluginPackage -PluginPath $PluginPath -RepositoryRoot $repoRoot `
    -RequireBinaries:$RequireBinaries -SkipRepositoryChecks:$SkipRepositoryChecks | Out-Null
if (-not $SkipRepositoryChecks) {
    Get-CaveRepositoryMarketplace -RepositoryRoot $repoRoot -PluginPath $PluginPath | Out-Null
}
Write-Host "Plugin validation passed: $PluginPath" -ForegroundColor Green
