[CmdletBinding()]
param(
    [string] $Repository,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$wikiSource = Join-Path $repoRoot 'docs\wiki'
if ([string]::IsNullOrWhiteSpace($Repository)) {
    $Repository = (& gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Invalid or unavailable repository name '$Repository'."
}

$pages = @(Get-ChildItem -LiteralPath $wikiSource -Filter '*.md' -File | Sort-Object Name)
Write-Host "Wiki publication plan for $Repository"
$pages | ForEach-Object { Write-Host "  $($_.Name)" }
if (-not $Apply) {
    Write-Host 'Preview only. Enable the GitHub Wiki and pass -Apply to replace its Markdown pages.'
    exit 0
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cave-wiki-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    & git clone "https://github.com/$Repository.wiki.git" $temporaryRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to clone the GitHub Wiki. Confirm it is enabled and authentication is valid.'
    }

    $resolvedTemporaryRoot = (Resolve-Path -LiteralPath $temporaryRoot).Path
    if (-not $resolvedTemporaryRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to update a wiki outside the temporary directory.'
    }
    Get-ChildItem -LiteralPath $resolvedTemporaryRoot -Filter '*.md' -File | Remove-Item -Force
    Copy-Item -LiteralPath $pages.FullName -Destination $resolvedTemporaryRoot -Force

    Push-Location $resolvedTemporaryRoot
    try {
        & git add -- '*.md'
        & git diff --cached --quiet
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'GitHub Wiki already matches docs/wiki.' -ForegroundColor Green
            exit 0
        }
        & git commit -m 'docs: synchronize CAVE wiki source'
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to commit the wiki synchronization.'
        }
        & git push origin HEAD:master
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to push the GitHub Wiki.'
        }
    }
    finally {
        Pop-Location
    }
    Write-Host 'GitHub Wiki synchronized from docs/wiki.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
