[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$pluginPath = Join-Path $repoRoot 'plugins\cave'
$marketplacePath = Join-Path $repoRoot '.agents\plugins\marketplace.json'
$pluginCreatorRoot = Join-Path $env:USERPROFILE '.codex\skills\.system\plugin-creator'
$cachebusterScript = Join-Path $pluginCreatorRoot 'scripts\update_plugin_cachebuster.py'
$marketplaceNameScript = Join-Path $pluginCreatorRoot 'scripts\read_marketplace_name.py'
$validationScript = Join-Path $PSScriptRoot 'validate-plugin.ps1'

foreach ($requiredPath in @($pluginPath, $marketplacePath, $cachebusterScript, $marketplaceNameScript, $validationScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required plugin-development path is missing: $requiredPath"
    }
}

$pluginServerPath = Join-Path $pluginPath 'server'
$viewerPort = 5098
$viewerBaseUri = "http://127.0.0.1:$viewerPort"
$pluginCacheRoot = Join-Path $env:USERPROFILE '.codex\plugins\cache\personal\cave'
$machineCaveRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'CAVE'
$machineViewerRoot = Join-Path $machineCaveRoot 'viewer'
$machineViewerExecutable = Join-Path $machineViewerRoot 'Cave.Host.exe'

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Command,

        [Parameter(Mandatory)]
        [string] $Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Test-PathBelow {
    param(
        [Parameter(Mandatory)]
        [string] $Candidate,

        [Parameter(Mandatory)]
        [string] $Root
    )

    $canonicalCandidate = [IO.Path]::GetFullPath($Candidate)
    $canonicalRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root)) + [IO.Path]::DirectorySeparatorChar
    return $canonicalCandidate.StartsWith($canonicalRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Backup-CaveHookRunners {
    if (-not (Test-Path -LiteralPath $pluginCacheRoot -PathType Container)) {
        return @()
    }

    $backupRoot = Join-Path ([IO.Path]::GetTempPath()) "cave-hook-runners-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    $backups = @()
    $index = 0
    foreach ($versionDirectory in Get-ChildItem -LiteralPath $pluginCacheRoot -Directory -ErrorAction SilentlyContinue) {
        $runnerPath = Join-Path $versionDirectory.FullName 'scripts\cave_activity_hook.py'
        if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
            continue
        }

        $backupPath = Join-Path $backupRoot "$index.py"
        Copy-Item -LiteralPath $runnerPath -Destination $backupPath
        $backups += [pscustomobject]@{
            BackupRoot = $backupRoot
            BackupPath = $backupPath
            RunnerPath = $runnerPath
        }
        $index++
    }

    if ($backups.Count -eq 0) {
        Remove-Item -LiteralPath $backupRoot
    }
    return $backups
}

function Restore-CaveHookRunners {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Backups
    )

    foreach ($backup in $Backups) {
        if (-not (Test-Path -LiteralPath $backup.RunnerPath -PathType Leaf)) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup.RunnerPath) -Force | Out-Null
            Copy-Item -LiteralPath $backup.BackupPath -Destination $backup.RunnerPath
        }
    }
}

function Update-CaveHookRunners {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Backups,

        [Parameter(Mandatory)]
        [string] $SourcePath
    )

    foreach ($backup in $Backups) {
        # CONSTRAINT: active Codex tasks retain the PLUGIN_ROOT resolved when they
        # started. Refresh the backward-compatible runner at that stable path so a
        # local plugin reinstall repairs hooks without requiring a new task.
        Copy-Item -LiteralPath $SourcePath -Destination $backup.RunnerPath -Force
    }
}

function Remove-CaveHookRunnerBackups {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Backups
    )

    foreach ($backupPath in @($Backups.BackupPath | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath
        }
    }
    foreach ($backupRoot in @($Backups.BackupRoot | Sort-Object -Unique)) {
        if (Test-Path -LiteralPath $backupRoot -PathType Container) {
            if (-not (Test-PathBelow -Candidate $backupRoot -Root ([IO.Path]::GetTempPath()))) {
                throw "Refusing to remove hook-runner backup outside the temporary directory: $backupRoot"
            }
            Remove-Item -LiteralPath $backupRoot
        }
    }
}

function Stop-StableCaveViewerProcesses {
    foreach ($candidate in @(Get-Process -Name 'Cave.Host' -ErrorAction SilentlyContinue)) {
        $candidatePath = try { $candidate.Path } catch { $null }
        if ([string]::IsNullOrWhiteSpace($candidatePath) -or
            -not (Test-PathBelow -Candidate $candidatePath -Root $machineViewerRoot)) {
            continue
        }

        Stop-Process -Id $candidate.Id -ErrorAction Stop
        $candidate.WaitForExit(5000) | Out-Null
        Write-Host "Stopped stable CAVE viewer process $($candidate.Id)."
    }
}

function Stop-CurrentCaveViewer {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $viewerPort -ErrorAction SilentlyContinue |
        Sort-Object OwningProcess -Unique)
    if ($listeners.Count -eq 0) {
        Stop-StableCaveViewerProcesses
        return
    }

    if ($listeners.Count -ne 1) {
        throw "More than one process is listening on CAVE port $viewerPort; refusing an ambiguous restart."
    }

    $listener = $listeners[0]
    $viewerProcess = Get-Process -Id $listener.OwningProcess -ErrorAction Stop
    $viewerPath = $viewerProcess.Path
    $isKnownViewer = $viewerProcess.ProcessName -eq 'Cave.Host' -and
        -not [string]::IsNullOrWhiteSpace($viewerPath) -and
        ((Test-PathBelow -Candidate $viewerPath -Root $repoRoot) -or
         (Test-PathBelow -Candidate $viewerPath -Root $pluginCacheRoot) -or
         (Test-PathBelow -Candidate $viewerPath -Root $machineViewerRoot))
    if (-not $isKnownViewer) {
        throw "Port $viewerPort is owned by '$($viewerProcess.ProcessName)' at '$viewerPath'; refusing to stop an unrelated process."
    }

    Stop-Process -Id $viewerProcess.Id
    $viewerProcess.WaitForExit(5000) | Out-Null
    Write-Host "Stopped stale CAVE viewer process $($viewerProcess.Id)."
    Stop-StableCaveViewerProcesses
}

function Install-StableCaveViewer {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot
    )

    if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
        throw "The installed CAVE server payload is missing: $SourceRoot"
    }
    if (-not (Test-PathBelow -Candidate $machineViewerRoot -Root $machineCaveRoot)) {
        throw "Refusing to install the machine viewer outside the CAVE application-data root: $machineViewerRoot"
    }

    New-Item -ItemType Directory -Path $machineViewerRoot -Force | Out-Null
    foreach ($attempt in 1..5) {
        try {
            Get-ChildItem -LiteralPath $SourceRoot -Force |
                Copy-Item -Destination $machineViewerRoot -Recurse -Force -ErrorAction Stop
            break
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }

            # A Codex rehook may revive the stable host while an update is being copied.
            # Stop every process from the verified stable directory and retry the same payload.
            Stop-StableCaveViewerProcesses
            Start-Sleep -Milliseconds 200
        }
    }

    if (-not (Test-Path -LiteralPath $machineViewerExecutable -PathType Leaf)) {
        throw "The stable CAVE viewer installation is missing: $machineViewerExecutable"
    }

    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $SourceRoot 'Cave.Host.exe') -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath $machineViewerExecutable -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) {
        throw 'The stable CAVE viewer executable does not match the installed plugin payload.'
    }
}

function Start-InstalledCaveViewer {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath
    )

    $viewerProcess = Start-Process `
        -FilePath $ExecutablePath `
        -WorkingDirectory (Split-Path -Parent $ExecutablePath) `
        -WindowStyle Hidden `
        -PassThru
    foreach ($attempt in 1..40) {
        if ($viewerProcess.HasExited) {
            throw "The installed CAVE viewer exited during restart with code $($viewerProcess.ExitCode)."
        }

        try {
            $runtime = Invoke-RestMethod -Uri "$viewerBaseUri/api/runtime" -TimeoutSec 2
            $health = Invoke-RestMethod -Uri "$viewerBaseUri/health" -TimeoutSec 2
            if ($runtime.viewerProtocolVersion -eq 2 -and ([string] $health).Trim() -eq 'Healthy') {
                Write-Host "Started compatible CAVE viewer process $($viewerProcess.Id) at $viewerBaseUri."
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if (-not $viewerProcess.HasExited) {
        Stop-Process -Id $viewerProcess.Id
    }
    throw 'The installed CAVE viewer did not become healthy within 10 seconds.'
}

Push-Location $repoRoot
try {
    Invoke-CheckedNative {
        npm --prefix src/Cave.Ui run build
    } 'CAVE UI build'

    Invoke-CheckedNative {
        dotnet publish src/Cave.Mcp/Cave.Mcp.csproj --configuration Release --runtime win-x64 --self-contained false --output $pluginServerPath
    } 'CAVE MCP server publish'

    Invoke-CheckedNative {
        dotnet publish src/Cave.Host/Cave.Host.csproj --configuration Release --runtime win-x64 --self-contained false --output $pluginServerPath
    } 'CAVE machine viewer publish'

    foreach ($requiredExecutable in @('Cave.Mcp.exe', 'Cave.Host.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $pluginServerPath $requiredExecutable))) {
            throw "The published plugin is missing $requiredExecutable."
        }
    }
}
finally {
    Pop-Location
}

$manifestPath = Join-Path $pluginPath '.codex-plugin\plugin.json'
$currentPluginVersion = (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).version
$nextCachebusterValue = [Int64]::Parse(
    [DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss', [Globalization.CultureInfo]::InvariantCulture),
    [Globalization.CultureInfo]::InvariantCulture)
$currentCachebusterMatch = [regex]::Match([string] $currentPluginVersion, '\+codex\.(\d{14})$')
if ($currentCachebusterMatch.Success) {
    $existingCachebusterValue = [Int64]::Parse(
        $currentCachebusterMatch.Groups[1].Value,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($existingCachebusterValue -ge $nextCachebusterValue) {
        $nextCachebusterValue = $existingCachebusterValue + 1
    }
}
$nextCachebuster = $nextCachebusterValue.ToString('00000000000000', [Globalization.CultureInfo]::InvariantCulture)

Invoke-CheckedNative {
    # CONSTRAINT: cache paths are version-addressed. Keep the suffix increasing even when an
    # existing manifest came from a clock or timezone ahead of the current UTC timestamp.
    python $cachebusterScript $pluginPath --cachebuster $nextCachebuster
} 'Plugin cache-buster update'

Invoke-CheckedNative {
    pwsh -NoProfile -File $validationScript -PluginPath $pluginPath
} 'Plugin validation'

$marketplaceName = (& python $marketplaceNameScript --marketplace-path $marketplacePath).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($marketplaceName)) {
    throw 'Unable to read the repository marketplace name.'
}

$marketplaceState = codex plugin marketplace list --json | ConvertFrom-Json
$registeredMarketplace = $marketplaceState.marketplaces | Where-Object {
    $_.name -eq $marketplaceName -and (Resolve-Path -LiteralPath $_.root).Path -eq $repoRoot
}

if ($null -eq $registeredMarketplace) {
    Invoke-CheckedNative {
        codex plugin marketplace add $repoRoot --json
    } 'Repository marketplace registration'
}

$hookRunnerBackups = @(Backup-CaveHookRunners)
$pluginInstalled = $false
try {
    Invoke-CheckedNative {
        codex plugin add "cave@$marketplaceName" --json
    } 'CAVE plugin installation'
    $pluginInstalled = $true
}
finally {
    # CONSTRAINT: Codex resolves PLUGIN_ROOT when a task starts. Reinstalling the plugin removes the
    # versioned cache path, so preserve its tiny hook runner until every older task has naturally ended.
    Restore-CaveHookRunners -Backups $hookRunnerBackups
    Remove-CaveHookRunnerBackups -Backups $hookRunnerBackups
}

if ($pluginInstalled) {
    Update-CaveHookRunners `
        -Backups $hookRunnerBackups `
        -SourcePath (Join-Path $pluginPath 'scripts\cave_activity_hook.py')
}

foreach ($hookRunnerBackup in $hookRunnerBackups) {
    if (-not (Test-Path -LiteralPath $hookRunnerBackup.RunnerPath -PathType Leaf)) {
        throw "The plugin update severed an active-task hook runner: $($hookRunnerBackup.RunnerPath)"
    }
}
if ($hookRunnerBackups.Count -gt 0) {
    Write-Host "Preserved and refreshed $($hookRunnerBackups.Count) version-addressed CAVE hook runner(s) for active Codex tasks."
}

$installedPlugin = (codex plugin list --json | ConvertFrom-Json).installed | Where-Object {
    $_.pluginId -eq "cave@$marketplaceName" -and $_.installed -and $_.enabled
} | Select-Object -First 1
if ($null -eq $installedPlugin -or [string]::IsNullOrWhiteSpace($installedPlugin.version)) {
    throw 'Codex did not report the newly installed CAVE plugin version.'
}

$installedServerRoot = Join-Path $pluginCacheRoot "$($installedPlugin.version)\server"
$installedViewer = Join-Path $installedServerRoot 'Cave.Host.exe'
if (-not (Test-Path -LiteralPath $installedViewer -PathType Leaf)) {
    throw "The installed CAVE viewer is missing: $installedViewer"
}

Stop-CurrentCaveViewer
Install-StableCaveViewer -SourceRoot $installedServerRoot
Start-InstalledCaveViewer -ExecutablePath $machineViewerExecutable

Write-Host "Installed cave@$marketplaceName from $pluginPath"
Write-Host 'Start a new Codex task to load updated plugin assets. If /hooks reports review required, review and trust the changed CAVE definition once.'
