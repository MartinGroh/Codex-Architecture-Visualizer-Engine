using Cave.Application;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies synthetic account-usage values so demo media never exposes an operator's Codex account.
/// </summary>
/// <param name="timeProvider">The clock used for the demo retrieval timestamp.</param>
public sealed class DemoCodexUsageProvider(TimeProvider timeProvider) : ICodexUsageProvider
{
    /// <inheritdoc />
    public Task<CodexUsageSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return Task.FromResult(new CodexUsageSnapshot(
            CodexUsageStatus.Ready,
            new CodexUsageSummary(
                LifetimeTokens: 8_420_000,
                PeakDailyTokens: 286_000,
                LongestRunningTurnSeconds: 1_284,
                CurrentStreakDays: 12,
                LongestStreakDays: 34),
            [
                new CodexDailyUsage(today.AddDays(-4), 118_000),
                new CodexDailyUsage(today.AddDays(-3), 164_000),
                new CodexDailyUsage(today.AddDays(-2), 142_000),
                new CodexDailyUsage(today.AddDays(-1), 221_000),
                new CodexDailyUsage(today, 96_000),
            ],
            [
                new CodexRateLimitWindow(
                    "codex",
                    "Codex",
                    "primary",
                    UsedPercent: 38,
                    WindowDurationMinutes: 10_080,
                    ResetsAtUtc: timeProvider.GetUtcNow().AddDays(4)),
            ],
            timeProvider.GetUtcNow(),
            Error: null));
    }
}
