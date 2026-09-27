using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Produces presentation snapshots from the single configured semantic provider.
/// </summary>
/// <param name="semanticIndex">The configured semantic-index implementation.</param>
/// <param name="gitDeltas">The application service that projects independent Git evidence.</param>
/// <param name="activityStore">The independent observed-activity and declared-scope store.</param>
/// <param name="conversationStore">The independent opt-in public conversation store.</param>
/// <param name="timeProvider">The authoritative clock for snapshot timestamps.</param>
/// <param name="goalProvider">The read-only native goal adapter for an exactly bound task.</param>
public sealed class ArchitectureSnapshotService(
    ISemanticIndex semanticIndex,
    GitDeltaService gitDeltas,
    IAgentActivityStore activityStore,
    IConversationStore conversationStore,
    TimeProvider timeProvider,
    ICodexGoalProvider goalProvider)
{
    /// <summary>
    /// Gets the current graph with source and freshness metadata.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root to inspect.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current architecture snapshot.</returns>
    public async Task<ArchitectureSnapshot> GetAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var result = await semanticIndex.ReadAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var metadata = new SnapshotMetadata(
            result.WorkspaceName,
            result.ProviderId,
            result.SourceKind,
            result.IsLive,
            timeProvider.GetUtcNow());
        var git = await gitDeltas.GetAsync(workspaceRoot, result.Graph, cancellationToken)
            .ConfigureAwait(false);
        var activity = await activityStore.ReadAsync(workspaceRoot, result.Graph, cancellationToken)
            .ConfigureAwait(false);
        var conversation = await ReadConversationAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);

        return new ArchitectureSnapshot(metadata, result.Graph, git, activity, conversation);
    }

    /// <summary>
    /// Refreshes the live activity and conversation overlays against an already-current semantic graph.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="current">The current stable snapshot.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The snapshot with current live overlays and unchanged semantic and Git evidence.</returns>
    public async Task<ArchitectureSnapshot> RefreshLiveOverlaysAsync(
        string workspaceRoot,
        ArchitectureSnapshot current,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(current);

        var activity = await activityStore.ReadAsync(workspaceRoot, current.Graph, cancellationToken)
            .ConfigureAwait(false);
        var conversation = await ReadConversationAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        return current with { Activity = activity, Conversation = conversation };
    }

    private async Task<ConversationOverlay> ReadConversationAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
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
            goal = new CodexGoalSnapshot(
                CodexGoalSourceStatus.Unavailable,
                sessionId,
                null,
                timeProvider.GetUtcNow(),
                "The native goal did not match the exact workspace task.");
        }

        return conversation with { Goal = goal };
    }
}
