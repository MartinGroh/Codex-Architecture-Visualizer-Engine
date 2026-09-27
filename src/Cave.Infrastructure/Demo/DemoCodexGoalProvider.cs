using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>Supplies an explicit synthetic goal for the fixed demo without contacting a real Codex task.</summary>
/// <param name="timeProvider">The clock for synthetic lookup timestamps.</param>
public sealed class DemoCodexGoalProvider(TimeProvider timeProvider) : ICodexGoalProvider
{
    /// <inheritdoc />
    public Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(sessionId == "demo-session"
            ? new CodexGoalSnapshot(CodexGoalSourceStatus.Ready, sessionId,
                new CodexGoal("Make architecture changes and agent work easy to follow", CodexGoalStatus.Active,
                    null, 2_450, 420, 1_790_507_000, 1_790_507_420), timeProvider.GetUtcNow(), null)
            : new CodexGoalSnapshot(CodexGoalSourceStatus.Unavailable, sessionId, null,
                timeProvider.GetUtcNow(), "The synthetic demo goal belongs only to demo-session."));
    }
}
