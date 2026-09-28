using Cave.Domain;

namespace Cave.Application;

/// <summary>Identifies the explicit configured source mode of the lightweight Agent Flow feed.</summary>
public enum AgentFlowSourceMode
{
    /// <summary>The feed reads real workspace activity and opt-in native goal state.</summary>
    Live,
    /// <summary>The feed reads only explicitly configured synthetic demo data.</summary>
    Demo,
}

/// <summary>Describes main-goal privacy, exact-task binding and availability independently from activity.</summary>
public enum AgentFlowGoalSourceStatus
{
    /// <summary>Conversation sharing is disabled; the objective is private.</summary>
    Private,
    /// <summary>Sharing is enabled but no exact workspace task is bound.</summary>
    Unbound,
    /// <summary>The exact native goal was read, possibly with no goal.</summary>
    Ready,
    /// <summary>The bound task's goal is unavailable or its binding changed during lookup.</summary>
    Unavailable,
}

/// <summary>Projects bounded activity and the shared main goal without acquiring a semantic graph or monitor.</summary>
/// <param name="activity">The existing bounded activity-only projection.</param>
/// <param name="conversations">The canonical shared-conversation and exact-task goal privacy policy.</param>
/// <param name="sourceMode">The mode explicitly selected by the composition root.</param>
public sealed class WorkspaceAgentFlowService(
    WorkspaceActivityService activity,
    WorkspaceConversationService conversations,
    AgentFlowSourceMode sourceMode)
{
    /// <summary>Gets the maximum retained objective length in UTF-16 characters.</summary>
    public const int MaximumObjectiveLength = 2_048;

    /// <summary>Reads the selected workspace's lightweight flow without exposing native task identifiers or chat.</summary>
    /// <param name="workspace">The already resolved opaque catalog selection.</param>
    /// <param name="cancellationToken">Signals that all underlying reads should stop.</param>
    /// <returns>The versioned, bounded activity and main-goal projection.</returns>
    public async Task<WorkspaceAgentFlowSnapshot> ReadAsync(
        WorkspaceCatalogEntry workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        var currentActivity = await activity.ReadAsync(workspace, cancellationToken).ConfigureAwait(false);
        var conversation = await conversations.ReadAsync(workspace.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var agents = currentActivity.Agents.Select(agent => new WorkspaceAgentFlowAgent(
            AgentDisplayNames.PublicId(agent.AgentId),
            AgentDisplayNames.Get(agent.AgentId, agent.IsSubagent),
            agent.AgentType,
            agent.IsSubagent,
            agent.State,
            agent.Phase,
            agent.Summary,
            agent.SummaryEvidence == AgentActivityEvidenceKind.Declared ? agent.Summary : null,
            agent.SummaryEvidence == AgentActivityEvidenceKind.Declared && agent.Summary is not null
                ? AgentActivityEvidenceKind.Declared : null,
            agent.StartedAtUtc,
            agent.UpdatedAtUtc,
            agent.Evidence,
            agent.SummaryEvidence)
        {
            ParentAgentId = agent.ParentAgentId is null ? null : AgentDisplayNames.PublicId(agent.ParentAgentId),
            LastObservedActivity = agent.LastObservedActivity,
        }).ToArray();

        return new WorkspaceAgentFlowSnapshot(1, currentActivity.WorkspaceId, currentActivity.WorkspaceName,
            sourceMode, currentActivity.GeneratedAtUtc, currentActivity.SourceUpdatedAtUtc,
            currentActivity.SourceStatus,
            currentActivity.Error is null ? null : "Some workspace activity evidence could not be read.",
            currentActivity.TotalAgentCount, agents, ProjectMainGoal(conversation));
    }

    private static WorkspaceAgentFlowMainGoal ProjectMainGoal(ConversationOverlay conversation)
    {
        if (!conversation.SharingEnabled)
        {
            return new(AgentFlowGoalSourceStatus.Private, null, null, null);
        }

        var sessionId = conversation.Control.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return new(AgentFlowGoalSourceStatus.Unbound, null, null,
                "No exact Codex task is bound to this workspace.");
        }

        if (conversation.Goal is not { Status: CodexGoalSourceStatus.Ready } result
            || !string.Equals(result.SessionId, sessionId, StringComparison.Ordinal))
        {
            return new(AgentFlowGoalSourceStatus.Unavailable, null, conversation.Goal?.RetrievedAtUtc,
                "The native goal is unavailable for the exact workspace task.");
        }

        WorkspaceAgentFlowGoal? goal = null;
        if (result.Goal is { } native)
        {
            var length = Math.Min(native.Objective.Length, MaximumObjectiveLength);
            if (length < native.Objective.Length && char.IsHighSurrogate(native.Objective[length - 1]))
            {
                length--;
            }

            goal = new(native.Objective[..length], length < native.Objective.Length, native.Status,
                native.TokenBudget, native.TokensUsed, native.TimeUsedSeconds, native.CreatedAt, native.UpdatedAt);
        }

        return new(AgentFlowGoalSourceStatus.Ready, goal, result.RetrievedAtUtc, null);
    }
}

/// <summary>Contains the versioned Agent Flow Light feed, with no semantic graph, native task ids or chat content.</summary>
/// <param name="SchemaVersion">The external contract version, currently 1.</param>
/// <param name="WorkspaceId">The opaque catalog identity, bounded to 128 characters.</param>
/// <param name="WorkspaceName">The display name, bounded to 160 UTF-16 characters.</param>
/// <param name="SourceMode">The explicitly configured live or synthetic demo mode.</param>
/// <param name="GeneratedAtUtc">The bounded activity projection's generation time.</param>
/// <param name="SourceUpdatedAtUtc">The latest activity evidence timestamp, or null.</param>
/// <param name="SourceStatus">The health of the independent activity evidence source.</param>
/// <param name="Error">A safe activity diagnostic without filesystem paths, or null.</param>
/// <param name="TotalAgentCount">The agent count before the existing 32-row limit.</param>
/// <param name="Agents">At most 32 agents, ordered by the canonical activity projection.</param>
/// <param name="MainGoal">The sharing-controlled native main goal; no per-agent goal is inferred.</param>
public sealed record WorkspaceAgentFlowSnapshot(
    int SchemaVersion,
    string WorkspaceId,
    string WorkspaceName,
    AgentFlowSourceMode SourceMode,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    AgentActivitySourceStatus SourceStatus,
    string? Error,
    int TotalAgentCount,
    IReadOnlyList<WorkspaceAgentFlowAgent> Agents,
    WorkspaceAgentFlowMainGoal MainGoal);

/// <summary>Contains one bounded activity row with an external correlation key and declared focus.</summary>
/// <param name="AgentId">The lowercase SHA256 correlation key; never a native Codex task identifier.</param>
/// <param name="DisplayName">The canonical friendly name derived from the original agent identity.</param>
/// <param name="AgentType">The source-provided role, bounded to 64 UTF-16 characters.</param>
/// <param name="IsSubagent">Whether observed evidence identifies a child agent.</param>
/// <param name="State">The canonical activity lifecycle.</param>
/// <param name="Phase">The current phase, or null when inactive or unknown.</param>
/// <param name="Summary">The latest work summary, bounded to 160 UTF-16 characters.</param>
/// <param name="CurrentFocus">The declared summary only, or null; never an inferred child goal.</param>
/// <param name="FocusEvidence">Declared when current focus exists; otherwise null.</param>
/// <param name="StartedAtUtc">The earliest retained activity time.</param>
/// <param name="UpdatedAtUtc">The latest retained state or scope evidence time.</param>
/// <param name="Evidence">Current lifecycle/phase provenance, or null.</param>
/// <param name="SummaryEvidence">Summary provenance, which can differ from lifecycle provenance.</param>
public sealed record WorkspaceAgentFlowAgent(
    string AgentId,
    string DisplayName,
    string AgentType,
    bool IsSubagent,
    AgentWorkState State,
    AgentActivityPhase? Phase,
    string? Summary,
    string? CurrentFocus,
    AgentActivityEvidenceKind? FocusEvidence,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AgentActivityEvidenceKind? Evidence,
    AgentActivityEvidenceKind? SummaryEvidence)
{
    /// <summary>The opaque key of the exact observed parent session, when known.</summary>
    public string? ParentAgentId { get; init; }

    /// <summary>The last observed work action, excluding terminal lifecycle markers.</summary>
    public string? LastObservedActivity { get; init; }
}

/// <summary>Contains main-goal availability without exposing the exact-task binding or raw diagnostics.</summary>
/// <param name="Status">Private, unbound, ready or unavailable; Ready with null Goal means no goal.</param>
/// <param name="Goal">The bounded shared main goal, or null.</param>
/// <param name="RetrievedAtUtc">The native lookup time, or null when no lookup was publishable.</param>
/// <param name="Error">A safe diagnostic without filesystem paths or native task identifiers.</param>
public sealed record WorkspaceAgentFlowMainGoal(
    AgentFlowGoalSourceStatus Status,
    WorkspaceAgentFlowGoal? Goal,
    DateTimeOffset? RetrievedAtUtc,
    string? Error);

/// <summary>Contains the explicitly shared native main goal with a bounded objective.</summary>
/// <param name="Objective">The objective, limited to 2048 UTF-16 characters without splitting surrogate pairs.</param>
/// <param name="IsTruncated">Whether the external feed omitted objective characters.</param>
/// <param name="Status">The native goal lifecycle.</param>
/// <param name="TokenBudget">The optional positive token budget.</param>
/// <param name="TokensUsed">The native goal token count.</param>
/// <param name="TimeUsedSeconds">The native elapsed goal time in seconds.</param>
/// <param name="CreatedAt">The raw native creation timestamp; units are not inferred.</param>
/// <param name="UpdatedAt">The raw native update timestamp; units are not inferred.</param>
public sealed record WorkspaceAgentFlowGoal(
    string Objective,
    bool IsTruncated,
    CodexGoalStatus Status,
    long? TokenBudget,
    long TokensUsed,
    long TimeUsedSeconds,
    long CreatedAt,
    long UpdatedAt);
