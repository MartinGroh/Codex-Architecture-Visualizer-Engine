using Cave.Domain;

namespace Cave.Application;

/// <summary>Owns the bounded native identity contract for metadata-only collection.</summary>
public static class AgentTelemetryIdentity
{
    /// <summary>Accepts native task keys without allowing paths, controls or display aliases.</summary>
    public static bool IsNative(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 128
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':');
}

/// <summary>Reads bounded metadata and usage for exact, already observed native thread identities.</summary>
public interface IAgentTelemetrySource
{
    /// <summary>Reads at most 32 exact threads without loading conversation items or taking task ownership.</summary>
    /// <param name="threadIds">Distinct native thread identities supplied by observed activity.</param>
    /// <param name="cancellationToken">Cancels the complete bounded batch.</param>
    /// <returns>One identity-correlated result per requested thread, including unavailable results.</returns>
    Task<IReadOnlyList<AgentTelemetrySample>> ReadAsync(IReadOnlyList<string> threadIds, CancellationToken cancellationToken);
}

/// <summary>Persists canonical per-thread telemetry independently from bounded activity retention.</summary>
public interface IAgentTelemetryStore
{
    /// <summary>Reads retained machine-local records and explicit persistence diagnostics.</summary>
    Task<AgentTelemetryState> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Atomically applies observations through the canonical accounting policy.</summary>
    Task ObserveAsync(IReadOnlyList<AgentTelemetryObservation> observations, CancellationToken cancellationToken);
}

/// <summary>Contains nullable counters reported by the native thread billing route.</summary>
/// <param name="Model">The source-reported model for this usage group.</param>
/// <param name="ReasoningEffort">The source-reported reasoning effort for this usage group.</param>
/// <param name="InputTokens">Input tokens, including cached input, or null.</param>
/// <param name="CachedInputTokens">The cached portion of input, or null.</param>
/// <param name="OutputTokens">Output tokens, or null.</param>
/// <param name="TotalTokens">Source-reported total tokens, or null.</param>
public sealed record AgentTokenUsage(string? Model, string? ReasoningEffort,
    long? InputTokens, long? CachedInputTokens, long? OutputTokens, long? TotalTokens);

/// <summary>Contains a privacy-minimized native read; error text is fixed and contains no native payload.</summary>
/// <param name="ThreadId">The exact native identity requested.</param>
/// <param name="ParentThreadId">The native direct parent, when reported.</param>
/// <param name="Configuration">Current or latest persisted settings, when available.</param>
/// <param name="Usage">Cumulative source-estimated usage groups, or null when unavailable.</param>
/// <param name="CompletedDurationMs">Sum of available completed-turn durations; never model-attributed.</param>
/// <param name="CompletedTurnCount">Number of turns with reported completed durations.</param>
/// <param name="DurationComplete">Whether all turn pages and completed durations were available.</param>
/// <param name="RetrievedAtUtc">The native observation time.</param>
/// <param name="Error">A safe diagnostic for unavailable or partial reads.</param>
public sealed record AgentTelemetrySample(string ThreadId, string? ParentThreadId,
    AgentModelConfiguration? Configuration, IReadOnlyList<AgentTokenUsage>? Usage,
    long? CompletedDurationMs, int CompletedTurnCount, bool DurationComplete,
    DateTimeOffset RetrievedAtUtc, string? Error);

/// <summary>Combines exact native telemetry with independent workspace membership and activity evidence.</summary>
/// <param name="Sample">The exact native read.</param>
/// <param name="WorkspaceIds">Opaque workspace memberships established by observed hooks.</param>
/// <param name="IsSubagent">Whether observed activity identifies a child agent.</param>
/// <param name="IsActive">The canonical reducer's current liveness result.</param>
/// <param name="ActivityAtUtc">The latest activity evidence time, independent of polling.</param>
public sealed record AgentTelemetryObservation(AgentTelemetrySample Sample, IReadOnlyList<string> WorkspaceIds,
    bool IsSubagent, bool IsActive, DateTimeOffset ActivityAtUtc);

/// <summary>Records prospective active time observed under a particular configuration.</summary>
/// <param name="Model">Observed configured model, or null for an unattributed interval.</param>
/// <param name="ReasoningEffort">Observed configured effort, or null when unattributed.</param>
/// <param name="Milliseconds">Additive agent time; not CPU time or task wall time.</param>
public sealed record AgentObservedTime(string? Model, string? ReasoningEffort, long Milliseconds);

/// <summary>Contains one canonical retained thread record. Native identities never leave the application API boundary.</summary>
/// <param name="ThreadId">The private native collection key.</param>
/// <param name="ParentThreadId">The exact native direct parent, when known.</param>
/// <param name="WorkspaceIds">Observed opaque workspace memberships.</param>
/// <param name="IsSubagent">Whether this is an observed child agent.</param>
/// <param name="FirstSeenAtUtc">The start of retained CAVE coverage for this identity.</param>
/// <param name="LastSampleAtUtc">The latest native observation time.</param>
/// <param name="ActivityAtUtc">The latest independent activity evidence time.</param>
/// <param name="WasActive">Whether the last observation was active.</param>
/// <param name="Configuration">Last read configuration, cleared when unavailable.</param>
/// <param name="Usage">The last available cumulative native usage snapshot.</param>
/// <param name="UsageCurrent">Whether usage was available on the latest read.</param>
/// <param name="CompletedDurationMs">The last available native completed-turn duration sum.</param>
/// <param name="CompletedTurnCount">Number of known completed turns.</param>
/// <param name="DurationComplete">Whether the duration covers all native completed turns.</param>
/// <param name="ObservedTime">Prospective active-time buckets.</param>
/// <param name="Error">A safe partial-source diagnostic.</param>
public sealed record AgentTelemetryRecord(string ThreadId, string? ParentThreadId,
    IReadOnlyList<string> WorkspaceIds, bool IsSubagent, DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSampleAtUtc, DateTimeOffset ActivityAtUtc, bool WasActive,
    AgentModelConfiguration? Configuration, IReadOnlyList<AgentTokenUsage>? Usage, bool UsageCurrent,
    long? CompletedDurationMs, int CompletedTurnCount, bool DurationComplete,
    IReadOnlyList<AgentObservedTime> ObservedTime, string? Error);

/// <summary>Contains retained canonical records and persistence health.</summary>
/// <param name="Records">One record per exact native thread.</param>
/// <param name="Error">A safe diagnostic when records could not all be read.</param>
public sealed record AgentTelemetryState(IReadOnlyList<AgentTelemetryRecord> Records, string? Error)
{
    /// <summary>Whether all records are explicitly synthetic demo data.</summary>
    public bool IsSynthetic { get; init; }
}

/// <summary>Owns idempotent usage replacement and conservative prospective time attribution.</summary>
public static class AgentTelemetryAccounting
{
    /// <summary>Applies one observation without treating polling as additional native usage.</summary>
    public static AgentTelemetryRecord Apply(AgentTelemetryRecord? prior, AgentTelemetryObservation observation)
    {
        var sample = observation.Sample;
        if (prior is not null && !string.Equals(prior.ThreadId, sample.ThreadId, StringComparison.Ordinal))
            throw new ArgumentException("Telemetry identities must match.", nameof(observation));
        var memberships = (prior?.WorkspaceIds ?? []).Concat(observation.WorkspaceIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (prior is not null && sample.RetrievedAtUtc <= prior.LastSampleAtUtc)
            return prior with { WorkspaceIds = memberships };

        var buckets = (prior?.ObservedTime ?? []).ToList();
        if (prior is { WasActive: true })
        {
            var end = observation.IsActive ? sample.RetrievedAtUtc
                : observation.ActivityAtUtc < sample.RetrievedAtUtc ? observation.ActivityAtUtc : sample.RetrievedAtUtc;
            var elapsed = end - prior.LastSampleAtUtc;
            // CONSTRAINT: gaps and restarts are not model execution evidence. Adjacent samples only
            // measure observed activity under unchanged configured settings, not historical turn effort.
            if (elapsed > TimeSpan.Zero && sample.RetrievedAtUtc - prior.LastSampleAtUtc <= TimeSpan.FromSeconds(35))
            {
                var model = prior.Configuration?.Model == sample.Configuration?.Model ? sample.Configuration?.Model : null;
                var effort = model is not null && prior.Configuration?.ReasoningEffort == sample.Configuration?.ReasoningEffort
                    ? sample.Configuration?.ReasoningEffort : null;
                var index = buckets.FindIndex(bucket => bucket.Model == model && bucket.ReasoningEffort == effort);
                var milliseconds = (long)elapsed.TotalMilliseconds;
                if (index < 0) buckets.Add(new(model, effort, milliseconds));
                else buckets[index] = buckets[index] with { Milliseconds = checked(buckets[index].Milliseconds + milliseconds) };
            }
        }

        return new(sample.ThreadId, sample.ParentThreadId ?? prior?.ParentThreadId, memberships,
            observation.IsSubagent, prior?.FirstSeenAtUtc ?? sample.RetrievedAtUtc,
            sample.RetrievedAtUtc, observation.ActivityAtUtc, observation.IsActive, sample.Configuration,
            sample.Usage ?? prior?.Usage, sample.Usage is not null,
            sample.CompletedDurationMs ?? prior?.CompletedDurationMs,
            sample.CompletedDurationMs is null ? prior?.CompletedTurnCount ?? 0 : sample.CompletedTurnCount,
            sample.CompletedDurationMs is not null && sample.DurationComplete,
            buckets, sample.Error);
    }
}
