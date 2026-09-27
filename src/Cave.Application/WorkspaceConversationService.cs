using Cave.Domain;

namespace Cave.Application;

/// <summary>Owns the shared conversation and exact-task goal privacy policy for workspace projections.</summary>
/// <param name="conversationStore">The canonical opt-in conversation store and task binding.</param>
/// <param name="goalProvider">The read-only goal endpoint for an already identified task.</param>
/// <param name="timeProvider">The authoritative clock for unavailable lookup results.</param>
public sealed class WorkspaceConversationService(
    IConversationStore conversationStore,
    ICodexGoalProvider goalProvider,
    TimeProvider timeProvider)
{
    /// <summary>Reads shared conversation and an optional main goal, rechecking privacy and task identity after lookup.</summary>
    /// <param name="workspaceRoot">The absolute root of the already selected workspace.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current overlay with no goal when sharing is disabled or its exact task changed.</returns>
    public async Task<ConversationOverlay> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var conversation = await conversationStore.ReadAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!conversation.SharingEnabled || string.IsNullOrWhiteSpace(conversation.Control.SessionId))
        {
            return conversation with { Goal = null };
        }

        var sessionId = conversation.Control.SessionId;
        var goal = await goalProvider.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        // A process read may outlive a privacy change or the workspace's task binding.
        // Re-read the authoritative overlay before publishing any objective, including cached results.
        conversation = await conversationStore.ReadAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!conversation.SharingEnabled
            || !string.Equals(conversation.Control.SessionId, sessionId, StringComparison.Ordinal))
        {
            return conversation with { Goal = null };
        }

        if (!string.Equals(goal.SessionId, sessionId, StringComparison.Ordinal))
        {
            goal = new CodexGoalSnapshot(CodexGoalSourceStatus.Unavailable, sessionId, null,
                timeProvider.GetUtcNow(), "The native goal did not match the exact workspace task.");
        }
        else if (goal.Status != CodexGoalSourceStatus.Ready)
        {
            goal = goal with { Goal = null };
        }

        return conversation with { Goal = goal };
    }
}
