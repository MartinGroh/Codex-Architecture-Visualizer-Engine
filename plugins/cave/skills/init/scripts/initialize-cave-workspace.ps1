[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $WorkspacePath
)

$ErrorActionPreference = 'Stop'
$requiredCodeGraphVersion = '1.5.0'
$hookObservationScript = Join-Path $PSScriptRoot 'cave-hook-observation.ps1'

if (-not (Test-Path -LiteralPath $hookObservationScript -PathType Leaf)) {
    throw "CAVE hook observation support is missing: '$hookObservationScript'."
}

. $hookObservationScript

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Command,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $output = & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }

    return $output
}

if (-not (Test-Path -LiteralPath $WorkspacePath -PathType Container)) {
    throw "CAVE workspace '$WorkspacePath' does not exist or is not a directory."
}

$candidateRoot = (Resolve-Path -LiteralPath $WorkspacePath).Path
$git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $git) {
    throw 'CAVE initialization requires Git, but git was not found on PATH.'
}

$gitRootOutput = & $git.Source -C $candidateRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or $null -eq $gitRootOutput) {
    throw "CAVE initialization requires a Git repository; '$candidateRoot' is not inside one."
}

$workspaceRoot = [IO.Path]::GetFullPath(($gitRootOutput | Select-Object -First 1).Trim())
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$codeGraphCommand = Join-Path $localAppData 'codegraph\current\bin\codegraph.cmd'
if (-not (Test-Path -LiteralPath $codeGraphCommand -PathType Leaf)) {
    throw "CAVE requires the pinned CodeGraph command at '$codeGraphCommand'."
}

$versionOutput = Invoke-CheckedNative {
    & $codeGraphCommand version
} 'Reading the CodeGraph version'
$codeGraphVersion = ($versionOutput | Select-Object -Last 1).Trim()
if ($codeGraphVersion -ne $requiredCodeGraphVersion) {
    throw "CAVE requires CodeGraph $requiredCodeGraphVersion, but '$codeGraphVersion' is installed at '$codeGraphCommand'."
}

$ignoreEntries = @(
    [pscustomobject]@{ Target = '.codegraph'; Pattern = '.codegraph/' }
    [pscustomobject]@{ Target = '.cave'; Pattern = '.cave/' }
)
$addedIgnorePatterns = [Collections.Generic.List[string]]::new()

foreach ($entry in $ignoreEntries) {
    & $git.Source -C $workspaceRoot check-ignore --quiet --no-index -- $entry.Target 2>$null
    if ($LASTEXITCODE -ne 0) {
        $addedIgnorePatterns.Add($entry.Pattern)
    }
}

if ($addedIgnorePatterns.Count -gt 0) {
    $gitIgnorePath = Join-Path $workspaceRoot '.gitignore'
    $newLine = [Environment]::NewLine
    $prefix = ''
    $existingText = if (Test-Path -LiteralPath $gitIgnorePath -PathType Leaf) {
        [IO.File]::ReadAllText($gitIgnorePath)
    }
    else {
        ''
    }

    if ($existingText.Length -gt 0 -and -not $existingText.EndsWith("`n", [StringComparison]::Ordinal)) {
        $prefix = $newLine
    }

    $header = if ($existingText.Contains('# CAVE local state', [StringComparison]::Ordinal)) {
        ''
    }
    else {
        "# CAVE local state$newLine"
    }
    $block = $prefix + $header + ($addedIgnorePatterns -join $newLine) + $newLine
    [IO.File]::AppendAllText($gitIgnorePath, $block, [Text.UTF8Encoding]::new($false))
}

$indexPath = Join-Path $workspaceRoot '.codegraph'
if (Test-Path -LiteralPath $indexPath -PathType Container) {
    $null = Invoke-CheckedNative {
        & $codeGraphCommand status $workspaceRoot
    } 'Validating the existing CodeGraph index'
    $indexAction = 'reused'
}
else {
    $null = Invoke-CheckedNative {
        & $codeGraphCommand init $workspaceRoot
    } 'Creating the CodeGraph index'
    $indexAction = 'created'
}

$activityDirectory = Join-Path $workspaceRoot '.cave\activity\inbox'
$codexSessionId = if (-not [string]::IsNullOrWhiteSpace($env:CODEX_SESSION_ID)) {
    $env:CODEX_SESSION_ID.Trim()
}
elseif (-not [string]::IsNullOrWhiteSpace($env:CODEX_THREAD_ID)) {
    $env:CODEX_THREAD_ID.Trim()
}
else {
    $null
}
$hookObservation = Get-CaveHookObservation `
    -ActivityDirectory $activityDirectory `
    -CodexSessionId $codexSessionId

[pscustomobject]@{
    workspaceRoot = $workspaceRoot
    codeGraphVersion = $codeGraphVersion
    indexAction = $indexAction
    ignorePatternsAdded = @($addedIgnorePatterns)
    activityHookObserved = $hookObservation.IsCurrentSessionObserved
    activityHookStatus = $hookObservation.Status
    currentSessionLatestHookEventKind = $hookObservation.CurrentSessionLatestEventKind
    currentSessionLatestHookEventAtUtc = $hookObservation.CurrentSessionLatestEventAtUtc
    latestHookEventKind = $hookObservation.LatestEventKind
    latestHookEventAtUtc = $hookObservation.LatestEventAtUtc
    hookJournalErrorCount = $hookObservation.JournalErrorCount
} | ConvertTo-Json -Compress
