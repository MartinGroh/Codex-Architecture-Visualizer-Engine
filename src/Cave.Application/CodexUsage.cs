namespace Cave.Application;

/// <summary>
/// Describes whether account usage was available from the local Codex App Server.
/// </summary>
public enum CodexUsageStatus
{
    /// <summary>Current account usage was read successfully.</summary>
    Ready,

    /// <summary>The local Codex App Server or its account usage data was unavailable.</summary>
    Unavailable,
}

/// <summary>
/// Carries aggregate Codex account usage statistics.
/// </summary>
/// <param name="LifetimeTokens">The lifetime token count, or null when Codex does not report it.</param>
/// <param name="PeakDailyTokens">The highest daily token count, or null when unavailable.</param>
/// <param name="LongestRunningTurnSeconds">The longest recorded turn duration in seconds, or null when unavailable.</param>
/// <param name="CurrentStreakDays">The current consecutive-usage streak, or null when unavailable.</param>
/// <param name="LongestStreakDays">The longest consecutive-usage streak, or null when unavailable.</param>
public sealed record CodexUsageSummary(
    long? LifetimeTokens,
    long? PeakDailyTokens,
    long? LongestRunningTurnSeconds,
    long? CurrentStreakDays,
    long? LongestStreakDays);

/// <summary>
/// Carries one daily Codex usage bucket.
/// </summary>
/// <param name="Date">The UTC calendar date represented by the bucket.</param>
/// <param name="Tokens">The total tokens reported for that date.</param>
public sealed record CodexDailyUsage(DateOnly Date, long Tokens);

/// <summary>
/// Carries one Codex rate-limit window reported by the local App Server.
/// </summary>
/// <param name="LimitId">The stable App Server limit identifier.</param>
/// <param name="LimitName">The operator-facing limit name, when supplied.</param>
/// <param name="Window">Whether this is the primary or secondary window.</param>
/// <param name="UsedPercent">The consumed percentage in the current window.</param>
/// <param name="WindowDurationMinutes">The total window duration, when supplied.</param>
/// <param name="ResetsAtUtc">The reset instant, when supplied.</param>
public sealed record CodexRateLimitWindow(
    string LimitId,
    string? LimitName,
    string Window,
    double UsedPercent,
    long? WindowDurationMinutes,
    DateTimeOffset? ResetsAtUtc);

/// <summary>
/// Carries account usage independently from workspace architecture truth.
/// </summary>
/// <param name="Status">Whether usage is available.</param>
/// <param name="Summary">Aggregate account statistics, when available.</param>
/// <param name="Daily">The most recent bounded daily token buckets.</param>
/// <param name="RateLimits">Current App Server rate-limit windows.</param>
/// <param name="RetrievedAtUtc">The time CAVE read the App Server response.</param>
/// <param name="Error">A safe diagnostic, or <see langword="null"/> when ready.</param>
public sealed record CodexUsageSnapshot(
    CodexUsageStatus Status,
    CodexUsageSummary? Summary,
    IReadOnlyList<CodexDailyUsage> Daily,
    IReadOnlyList<CodexRateLimitWindow> RateLimits,
    DateTimeOffset RetrievedAtUtc,
    string? Error);

/// <summary>
/// Reads account usage from the canonical local Codex control plane.
/// </summary>
public interface ICodexUsageProvider
{
    /// <summary>
    /// Gets a bounded current account usage snapshot.
    /// </summary>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current usage snapshot or an explicit unavailable result.</returns>
    Task<CodexUsageSnapshot> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Carries application information displayed by both embedded and browser clients.
/// </summary>
/// <param name="ViewerProtocolVersion">The machine viewer protocol understood by this build.</param>
/// <param name="Usage">Current Codex account usage.</param>
public sealed record CaveInfoSnapshot(int ViewerProtocolVersion, CodexUsageSnapshot Usage);

/// <summary>
/// Produces the CAVE information view from independent application-owned sources.
/// </summary>
/// <param name="usageProvider">The configured Codex account usage provider.</param>
public sealed class CaveInfoService(ICodexUsageProvider usageProvider)
{
    /// <summary>
    /// Gets current CAVE and Codex account information.
    /// </summary>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current information snapshot.</returns>
    public async Task<CaveInfoSnapshot> GetAsync(CancellationToken cancellationToken) =>
        new(
            CaveRuntimeContract.ViewerProtocolVersion,
            await usageProvider.GetAsync(cancellationToken).ConfigureAwait(false));
}
