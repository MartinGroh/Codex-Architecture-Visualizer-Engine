[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$pluginRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$observationScript = Join-Path $pluginRoot 'skills\init\scripts\cave-hook-observation.ps1'
. $observationScript

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [AllowNull()] $Expected,
        [Parameter(Mandatory)] [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', received '$Actual'."
    }
}

function Write-ActivityEvent {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $EventId,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [DateTimeOffset] $OccurredAtUtc,
        [string] $Kind = 'PreToolUse'
    )

    $event = [ordered]@{
        schemaVersion = 1
        eventId = $EventId
        kind = $Kind
        occurredAtUtc = $OccurredAtUtc.ToUniversalTime().ToString('O')
        sessionId = $SessionId
        paths = @()
    }
    $path = Join-Path $Directory "$EventId.json"
    [IO.File]::WriteAllText(
        $path,
        ($event | ConvertTo-Json -Depth 4 -Compress),
        [Text.UTF8Encoding]::new($false))
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "cave-hook-observation-$([Guid]::NewGuid().ToString('N'))"
$resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $empty = Get-CaveHookObservation -ActivityDirectory $testRoot -CodexSessionId 'current'
    Assert-Equal $false $empty.IsCurrentSessionObserved 'An empty journal must not report active hooks.'
    Assert-Equal 'Unobserved' $empty.Status 'An empty journal has the wrong status.'

    Write-ActivityEvent -Directory $testRoot -EventId 'historical' -SessionId 'old' `
        -OccurredAtUtc ([DateTimeOffset]::UtcNow.AddMinutes(-1))
    $historical = Get-CaveHookObservation -ActivityDirectory $testRoot -CodexSessionId 'current'
    Assert-Equal $false $historical.IsCurrentSessionObserved 'Historical evidence must not validate this task.'
    Assert-Equal 'HistoricalOnly' $historical.Status 'Historical evidence has the wrong status.'

    Write-ActivityEvent -Directory $testRoot -EventId 'current' -SessionId 'current' `
        -OccurredAtUtc ([DateTimeOffset]::UtcNow) -Kind 'PostToolUse'
    [IO.File]::WriteAllText((Join-Path $testRoot 'malformed.json'), '{', [Text.UTF8Encoding]::new($false))
    $current = Get-CaveHookObservation -ActivityDirectory $testRoot -CodexSessionId 'current'
    Assert-Equal $true $current.IsCurrentSessionObserved 'Current-session evidence was not detected.'
    Assert-Equal 'CurrentSession' $current.Status 'Current-session evidence has the wrong status.'
    Assert-Equal 'PostToolUse' $current.CurrentSessionLatestEventKind 'The newest current event was not reported.'
    Assert-Equal 1 $current.JournalErrorCount 'Malformed records must be surfaced without masking valid evidence.'

    $identityUnavailable = Get-CaveHookObservation -ActivityDirectory $testRoot -CodexSessionId $null
    Assert-Equal $false $identityUnavailable.IsCurrentSessionObserved 'Missing identity must not claim current hooks.'
    Assert-Equal 'SessionIdentityUnavailable' $identityUnavailable.Status 'Missing identity has the wrong status.'

    Write-Host 'CAVE hook observation tests passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        $resolvedTestRoot = (Resolve-Path -LiteralPath $testRoot).Path
        if (-not $resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove hook-test directory outside the system temp root: '$resolvedTestRoot'."
        }

        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
