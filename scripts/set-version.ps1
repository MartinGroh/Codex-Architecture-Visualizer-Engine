[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') {
    throw "'$Version' is not a supported Semantic Version. Build metadata is reserved for installation cache-busters."
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$versionPath = Join-Path $repoRoot 'VERSION'
$currentVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()
$targets = @(
    'VERSION',
    'Directory.Build.props',
    'src/Cave.Ui/package.json',
    'src/Cave.Ui/package-lock.json',
    'plugins/cave/.codex-plugin/plugin.json'
)

Write-Host "Version plan: $currentVersion -> $Version"
$targets | ForEach-Object { Write-Host "  $_" }
if (-not $Apply) {
    Write-Host 'Preview only. Pass -Apply to update these files.'
    exit 0
}

function Replace-Checked {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Replacement,
        [int] $ExpectedCount = 1
    )

    $content = Get-Content -LiteralPath $Path -Raw
    $matches = [regex]::Matches($content, $Pattern)
    if ($matches.Count -ne $ExpectedCount) {
        throw "Expected $ExpectedCount version match(es) in '$Path', found $($matches.Count)."
    }
    $updated = [regex]::Replace($content, $Pattern, $Replacement)
    [IO.File]::WriteAllText($Path, $updated, [Text.UTF8Encoding]::new($false))
}

[IO.File]::WriteAllText($versionPath, "$Version`n", [Text.UTF8Encoding]::new($false))
Replace-Checked `
    -Path (Join-Path $repoRoot 'Directory.Build.props') `
    -Pattern "<VersionPrefix>$([regex]::Escape($currentVersion))</VersionPrefix>" `
    -Replacement "<VersionPrefix>$Version</VersionPrefix>"
Replace-Checked `
    -Path (Join-Path $repoRoot 'src\Cave.Ui\package.json') `
    -Pattern "`"version`":\s*`"$([regex]::Escape($currentVersion))`"" `
    -Replacement "`"version`": `"$Version`""
Replace-Checked `
    -Path (Join-Path $repoRoot 'src\Cave.Ui\package-lock.json') `
    -Pattern "`"version`":\s*`"$([regex]::Escape($currentVersion))`"" `
    -Replacement "`"version`": `"$Version`"" `
    -ExpectedCount 2
Replace-Checked `
    -Path (Join-Path $repoRoot 'plugins\cave\.codex-plugin\plugin.json') `
    -Pattern "`"version`":\s*`"$([regex]::Escape($currentVersion))(?:\+codex\.[0-9A-Za-z.-]+)?`"" `
    -Replacement "`"version`": `"$Version`""

& (Join-Path $PSScriptRoot 'validate-plugin-metadata.ps1')
if ($LASTEXITCODE -ne 0) {
    throw 'Version synchronization validation failed.'
}
Write-Host "Version updated to $Version. Update CHANGELOG.md before release." -ForegroundColor Green
