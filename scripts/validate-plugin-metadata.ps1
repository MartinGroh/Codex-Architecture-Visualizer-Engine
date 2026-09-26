[CmdletBinding()]
param(
    [string] $PluginPath,
    [switch] $RequireBinaries,
    [switch] $SkipRepositoryChecks
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PluginPath)) {
    $PluginPath = Join-Path $repoRoot 'plugins\cave'
}
$resolvedPluginPath = (Resolve-Path -LiteralPath $PluginPath).Path
$manifestPath = Join-Path $resolvedPluginPath '.codex-plugin\plugin.json'
$mcpPath = Join-Path $resolvedPluginPath '.mcp.json'
$hooksPath = Join-Path $resolvedPluginPath 'hooks\hooks.json'

foreach ($requiredPath in @($manifestPath, $mcpPath, $hooksPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required plugin file is missing: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.name -ne 'cave') {
    throw "Plugin name must be 'cave'."
}
if ([string]::IsNullOrWhiteSpace($manifest.description) -or $manifest.license -ne 'MIT') {
    throw 'Plugin description and MIT license metadata are required.'
}
if ($manifest.skills -ne './skills/' -or -not (Test-Path -LiteralPath (Join-Path $resolvedPluginPath 'skills'))) {
    throw 'Plugin skills metadata does not point to the skills directory.'
}

$mcp = Get-Content -LiteralPath $mcpPath -Raw | ConvertFrom-Json
$caveServer = $mcp.mcpServers.cave
if ($null -eq $caveServer -or $caveServer.command -ne './server/Cave.Mcp.exe') {
    throw 'The CAVE MCP server entry point is missing or unexpected.'
}

$hooks = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
foreach ($requiredEvent in @('SessionStart', 'SessionEnd', 'UserPromptSubmit', 'SubagentStart', 'SubagentStop', 'PreToolUse', 'PostToolUse', 'Stop')) {
    if ($null -eq $hooks.hooks.$requiredEvent) {
        throw "The plugin hook manifest is missing '$requiredEvent'."
    }
}

if ($RequireBinaries) {
    foreach ($binaryName in @('Cave.Mcp.exe', 'Cave.Host.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedPluginPath "server\$binaryName") -PathType Leaf)) {
            throw "Packaged plugin is missing server/$binaryName."
        }
    }
}

if (-not $SkipRepositoryChecks) {
    $version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
    if ($manifest.version -notmatch "^$([regex]::Escape($version))(?:\+codex\.[0-9A-Za-z.-]+)?$") {
        throw "Plugin version '$($manifest.version)' does not match repository VERSION '$version'."
    }

    $frontend = Get-Content -LiteralPath (Join-Path $repoRoot 'src\Cave.Ui\package.json') -Raw | ConvertFrom-Json
    if ($frontend.version -ne $version) {
        throw "Frontend version '$($frontend.version)' does not match repository VERSION '$version'."
    }

    [xml] $buildProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
    if ($buildProps.Project.PropertyGroup.VersionPrefix -ne $version) {
        throw ".NET VersionPrefix '$($buildProps.Project.PropertyGroup.VersionPrefix)' does not match repository VERSION '$version'."
    }
}

Write-Host "Plugin metadata validation passed: $resolvedPluginPath" -ForegroundColor Green
