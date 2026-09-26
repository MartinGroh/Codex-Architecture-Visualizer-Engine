[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Runtime = 'win-x64',

    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
$releaseRoot = Join-Path $repoRoot 'artifacts\releases'
$stagingRoot = Join-Path $repoRoot "artifacts\release-staging\$([guid]::NewGuid().ToString('N'))"
$pluginStage = Join-Path $stagingRoot 'cave'
$archiveName = "cave-$version-$Runtime.zip"
$archivePath = Join-Path $releaseRoot $archiveName
$checksumPath = "$archivePath.sha256"
$manifestOutputPath = Join-Path $releaseRoot "cave-$version-$Runtime.release.json"

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)] [scriptblock] $Command,
        [Parameter(Mandatory)] [string] $Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

New-Item -ItemType Directory -Path $releaseRoot, $pluginStage -Force | Out-Null
Push-Location $repoRoot
try {
    Invoke-CheckedNative { pwsh -NoProfile -File scripts/validate-plugin-metadata.ps1 } 'Repository version and plugin metadata validation'
    Invoke-CheckedNative { pwsh -NoProfile -File scripts/audit-public-readiness.ps1 } 'Public-readiness audit'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui ci } 'Frontend dependency restore'
    Invoke-CheckedNative { npm --prefix src/Cave.Ui run build } 'Frontend build'
    Invoke-CheckedNative { dotnet build CAVE.slnx --configuration $Configuration } '.NET build'
    if (-not $SkipTests) {
        Invoke-CheckedNative { npm --prefix src/Cave.Ui run test } 'Frontend tests'
        Invoke-CheckedNative { dotnet test CAVE.slnx --configuration $Configuration --no-build } '.NET tests'
    }

    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'plugins\cave') -Force `
        | Where-Object Name -ne 'server' `
        | Copy-Item -Destination $pluginStage -Recurse -Force

    $stagedManifestPath = Join-Path $pluginStage '.codex-plugin\plugin.json'
    $stagedManifest = Get-Content -LiteralPath $stagedManifestPath -Raw | ConvertFrom-Json
    $stagedManifest.version = $version
    [IO.File]::WriteAllText(
        $stagedManifestPath,
        "$($stagedManifest | ConvertTo-Json -Depth 20)`n",
        [Text.UTF8Encoding]::new($false)
    )

    $serverStage = Join-Path $pluginStage 'server'
    Invoke-CheckedNative {
        dotnet publish src/Cave.Mcp/Cave.Mcp.csproj `
            --configuration $Configuration `
            --runtime $Runtime `
            --self-contained false `
            -p:ContinuousIntegrationBuild=true `
            -p:DebugSymbols=false `
            -p:DebugType=None `
            -p:PathMap="$repoRoot=/_/" `
            --output $serverStage
    } 'CAVE MCP publish'
    Invoke-CheckedNative {
        dotnet publish src/Cave.Host/Cave.Host.csproj `
            --configuration $Configuration `
            --runtime $Runtime `
            --self-contained false `
            -p:ContinuousIntegrationBuild=true `
            -p:DebugSymbols=false `
            -p:DebugType=None `
            -p:PathMap="$repoRoot=/_/" `
            --output $serverStage
    } 'CAVE host publish'
    Invoke-CheckedNative {
        pwsh -NoProfile -File scripts/validate-plugin-metadata.ps1 `
            -PluginPath $pluginStage `
            -RequireBinaries `
            -SkipRepositoryChecks
    } 'Staged plugin validation'

    # Generated interpreter caches and local debug symbols can expose build-machine paths and do
    # not belong in a public plugin archive. Release symbols can be designed separately with
    # deterministic source mapping if maintainers decide to publish them later.
    Get-ChildItem -LiteralPath $pluginStage -Directory -Recurse -Force `
        | Where-Object Name -eq '__pycache__' `
        | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $pluginStage -File -Recurse -Force `
        | Where-Object { $_.Extension -in @('.pyc', '.pdb') -or $_.Name -eq 'appsettings.Development.json' } `
        | Remove-Item -Force

    $developerPathPattern = '(?i)\b[A-Z]:\\(?:Users|Code)\\'
    foreach ($publishedBinary in Get-ChildItem -LiteralPath $serverStage -File -Recurse -Include '*.dll', '*.exe') {
        $binaryText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($publishedBinary.FullName))
        if ($binaryText -match $developerPathPattern) {
            throw "Published binary '$($publishedBinary.Name)' contains a build-machine absolute path."
        }
    }

    foreach ($outputPath in @($archivePath, $checksumPath, $manifestOutputPath)) {
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }
    }
    Compress-Archive -LiteralPath $pluginStage -DestinationPath $archivePath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $checksumPath,
        "$hash  $archiveName`n",
        [Text.UTF8Encoding]::new($false)
    )

    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the release commit.'
    }
    $releaseManifest = [ordered] @{
        name = 'cave'
        version = $version
        runtime = $Runtime
        commit = $commit
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        archive = $archiveName
        sha256 = $hash
    }
    [IO.File]::WriteAllText(
        $manifestOutputPath,
        "$($releaseManifest | ConvertTo-Json -Depth 5)`n",
        [Text.UTF8Encoding]::new($false)
    )

    Write-Host "Release package: $archivePath" -ForegroundColor Green
    Write-Host "SHA-256: $hash"
    Write-Host "Staging retained under ignored artifacts for inspection: $stagingRoot"
}
finally {
    Pop-Location
}
