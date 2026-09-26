[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $WorkspacePath,

    [switch] $BypassHookTrust,

    [ValidateRange(5, 120)]
    [int] $JournalTimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
$candidateRoot = (Resolve-Path -LiteralPath $WorkspacePath).Path
$git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
$codex = Get-Command codex -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $git) {
    throw 'Live CAVE hook verification requires git on PATH.'
}
if ($null -eq $codex) {
    throw 'Live CAVE hook verification requires the Codex CLI on PATH.'
}

$gitRootOutput = & $git.Source -C $candidateRoot rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or $null -eq $gitRootOutput) {
    throw "Live CAVE hook verification requires a Git repository; '$candidateRoot' is not inside one."
}
$workspaceRoot = [IO.Path]::GetFullPath(($gitRootOutput | Select-Object -First 1).Trim())

$arguments = @(
    '-a', 'never',
    'exec',
    '--ephemeral',
    '-C', $workspaceRoot,
    '-s', 'read-only',
    '--json'
)
if ($BypassHookTrust) {
    $arguments += '--dangerously-bypass-hook-trust'
}
$arguments += 'Use the shell tool exactly once to run Get-Location. Then reply exactly CAVE-HOOK-PROBE-OK.'

$output = @(& $codex.Source @arguments 2>&1)
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "The ephemeral Codex hook probe failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
}

$threadStarted = $output | ForEach-Object {
    $line = [string] $_
    if (-not $line.StartsWith('{', [StringComparison]::Ordinal)) {
        return
    }

    try {
        $event = $line | ConvertFrom-Json
        if ($event.type -eq 'thread.started') {
            $event
        }
    }
    catch {
        # Codex diagnostics can be interleaved with the JSONL protocol on stderr.
    }
} | Select-Object -First 1
if ($null -eq $threadStarted -or [string]::IsNullOrWhiteSpace($threadStarted.thread_id)) {
    throw "The Codex hook probe did not report its session id.`n$($output -join [Environment]::NewLine)"
}

$sessionId = [string] $threadStarted.thread_id
$activityDirectory = Join-Path $workspaceRoot '.cave\activity\inbox'
$requiredKinds = @('SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'Stop', 'SessionEnd')
$observedEvents = @()
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($JournalTimeoutSeconds)
do {
    $observedEvents = if (Test-Path -LiteralPath $activityDirectory -PathType Container) {
        @(Get-ChildItem -LiteralPath $activityDirectory -Filter '*.json' -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                try {
                    $event = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                    if ($event.sessionId -eq $sessionId) {
                        $event
                    }
                }
                catch {
                    # The live projection reports malformed records separately; keep polling valid probe evidence.
                }
            })
    }
    else {
        @()
    }

    $observedKinds = @($observedEvents.kind | Sort-Object -Unique)
    $missingKinds = @($requiredKinds | Where-Object { $_ -notin $observedKinds })
    if ($missingKinds.Count -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 100
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($missingKinds.Count -gt 0) {
    throw "Codex session '$sessionId' did not emit the required CAVE lifecycle hooks: $($missingKinds -join ', '). Observed: $($observedKinds -join ', ')."
}

[pscustomobject]@{
    workspaceRoot = $workspaceRoot
    sessionId = $sessionId
    hookTrustBypassed = [bool] $BypassHookTrust
    observedKinds = $observedKinds
    eventCount = $observedEvents.Count
    verified = $true
} | ConvertTo-Json -Compress
