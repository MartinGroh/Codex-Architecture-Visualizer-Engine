function Get-CaveHookObservation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $ActivityDirectory,

        [AllowNull()]
        [AllowEmptyString()]
        [string] $CodexSessionId,

        [ValidateRange(1, 2048)]
        [int] $MaximumEvents = 2048
    )

    $eventPaths = if (Test-Path -LiteralPath $ActivityDirectory -PathType Container) {
        @(Get-ChildItem -LiteralPath $ActivityDirectory -Filter '*.json' -File -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            Select-Object -First $MaximumEvents)
    }
    else {
        @()
    }

    $validEventCount = 0
    $journalErrorCount = 0
    $latestEvent = $null
    $latestCurrentSessionEvent = $null
    foreach ($eventPath in $eventPaths) {
        try {
            $activityEvent = Get-Content -LiteralPath $eventPath.FullName -Raw | ConvertFrom-Json
            if ($activityEvent.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace($activityEvent.eventId)) {
                $journalErrorCount++
                continue
            }

            $occurredAtUtc = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse(
                [string] $activityEvent.occurredAtUtc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref] $occurredAtUtc)) {
                $journalErrorCount++
                continue
            }

            $validEventCount++
            $candidate = [pscustomobject]@{
                Kind = [string] $activityEvent.kind
                OccurredAtUtc = $occurredAtUtc.ToUniversalTime()
                SessionId = [string] $activityEvent.sessionId
            }
            if ($null -eq $latestEvent -or $candidate.OccurredAtUtc -gt $latestEvent.OccurredAtUtc) {
                $latestEvent = $candidate
            }

            if (-not [string]::IsNullOrWhiteSpace($CodexSessionId) -and
                $candidate.SessionId.Equals($CodexSessionId.Trim(), [StringComparison]::Ordinal) -and
                ($null -eq $latestCurrentSessionEvent -or
                    $candidate.OccurredAtUtc -gt $latestCurrentSessionEvent.OccurredAtUtc)) {
                $latestCurrentSessionEvent = $candidate
            }
        }
        catch {
            # A partially copied or malformed record is projection evidence, not a reason to make init fail.
            $journalErrorCount++
        }
    }

    $status = if ($null -ne $latestCurrentSessionEvent) {
        'CurrentSession'
    }
    elseif ([string]::IsNullOrWhiteSpace($CodexSessionId)) {
        'SessionIdentityUnavailable'
    }
    elseif ($validEventCount -gt 0) {
        'HistoricalOnly'
    }
    else {
        'Unobserved'
    }

    [pscustomobject]@{
        IsCurrentSessionObserved = $null -ne $latestCurrentSessionEvent
        Status = $status
        CurrentSessionLatestEventKind = if ($null -eq $latestCurrentSessionEvent) { $null } else { $latestCurrentSessionEvent.Kind }
        CurrentSessionLatestEventAtUtc = if ($null -eq $latestCurrentSessionEvent) { $null } else { $latestCurrentSessionEvent.OccurredAtUtc }
        LatestEventKind = if ($null -eq $latestEvent) { $null } else { $latestEvent.Kind }
        LatestEventAtUtc = if ($null -eq $latestEvent) { $null } else { $latestEvent.OccurredAtUtc }
        ValidEventCount = $validEventCount
        JournalErrorCount = $journalErrorCount
    }
}
