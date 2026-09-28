using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Projects compact workspace activity for external displays without loading semantic graphs or conversations.
/// </summary>
/// <param name="activityStore">The canonical activity reducer shared with the full workspace view.</param>
/// <param name="timeProvider">The clock identifying when this projection was read.</param>
public sealed class WorkspaceActivityService(IAgentActivityStore activityStore, TimeProvider timeProvider)
{
    private static readonly ArchitectureGraph EmptyGraph = ArchitectureGraph.Create([], []);

    /// <summary>Gets the maximum number of agent rows returned in one feed response.</summary>
    public const int MaximumAgents = 32;

    /// <summary>
    /// Reads bounded activity for an already-resolved catalog entry. The caller owns workspace selection.
    /// </summary>
    /// <param name="workspace">The selected machine catalog entry.</param>
    /// <param name="cancellationToken">Signals that the activity read should stop.</param>
    /// <returns>Privacy-minimized activity with explicit source freshness and provenance.</returns>
    /// <exception cref="InvalidDataException">A retained identity cannot fit the bounded external contract.</exception>
    public async Task<WorkspaceActivitySnapshot> ReadAsync(
        WorkspaceCatalogEntry workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var activity = await activityStore.ReadAsync(workspace.WorkspaceRoot, EmptyGraph, cancellationToken)
            .ConfigureAwait(false);
        var selectedAgents = activity.Agents
            .OrderByDescending(agent => agent.State == AgentWorkState.Active)
            .ThenByDescending(agent => agent.UpdatedAtUtc)
            .ThenBy(agent => agent.AgentId, StringComparer.Ordinal)
            .Take(MaximumAgents)
            .Select(agent => new WorkspaceActivityAgent(
                RequireIdentity(agent.AgentId),
                Bound(agent.AgentType, 64)!,
                agent.IsSubagent,
                agent.State,
                agent.Phase,
                Bound(agent.Summary, 160),
                agent.StartedAtUtc,
                agent.UpdatedAtUtc,
                agent.Evidence,
                agent.SummaryEvidence)
            {
                ParentAgentId = agent.ParentAgentId is null ? null : RequireIdentity(agent.ParentAgentId),
                LastObservedActivity = Bound(agent.LastObservedActivity, 160),
            })
            .ToArray();
        var latest = activity.Agents.Select(agent => (DateTimeOffset?)agent.UpdatedAtUtc)
            .Append(activity.LatestInstruction?.ObservedAtUtc)
            .Max();

        return new WorkspaceActivitySnapshot(
            1,
            RequireIdentity(workspace.WorkspaceId),
            Bound(new DirectoryInfo(workspace.WorkspaceRoot).Name, 160)!,
            timeProvider.GetUtcNow(),
            latest,
            activity.SourceStatus,
            Bound(activity.Error, 512),
            activity.Agents.Count,
            selectedAgents);
    }

    private static string RequireIdentity(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            ? value
            : throw new InvalidDataException("An activity identity is empty or exceeds the 128-character feed limit.");

    private static string? Bound(string? value, int maximumLength)
    {
        if (value is null || value.Length <= maximumLength)
        {
            return value;
        }

        // Keep UTF-16 well-formed when the display text ends at a supplementary Unicode character.
        var length = char.IsHighSurrogate(value[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
        return value[..length];
    }
}

/// <summary>Contains the versioned, bounded activity-only workspace feed.</summary>
/// <param name="SchemaVersion">The external feed contract version.</param>
/// <param name="WorkspaceId">The opaque machine catalog identity.</param>
/// <param name="WorkspaceName">The display name, limited to 160 UTF-16 characters.</param>
/// <param name="GeneratedAtUtc">When the source projection completed, independent of agent activity age.</param>
/// <param name="SourceUpdatedAtUtc">The latest retained evidence time, or null when no evidence exists.</param>
/// <param name="SourceStatus">Whether activity evidence is unobserved, ready, or degraded.</param>
/// <param name="Error">A bounded diagnostic, or null when no source error was reported.</param>
/// <param name="TotalAgentCount">The retained agent count before the 32-row response limit.</param>
/// <param name="Agents">Active agents first, then newest evidence, with ordinal identity breaking ties.</param>
public sealed record WorkspaceActivitySnapshot(
    int SchemaVersion,
    string WorkspaceId,
    string WorkspaceName,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? SourceUpdatedAtUtc,
    AgentActivitySourceStatus SourceStatus,
    string? Error,
    int TotalAgentCount,
    IReadOnlyList<WorkspaceActivityAgent> Agents);

/// <summary>Provides one privacy-minimized agent row for an external display.</summary>
/// <param name="AgentId">The stable identity, never truncated or rewritten.</param>
/// <param name="AgentType">The type or role, limited to 64 UTF-16 characters.</param>
/// <param name="IsSubagent">Whether evidence identifies a child agent; exact observed parent identity is optional.</param>
/// <param name="State">The canonical lifecycle state, including the existing active lease.</param>
/// <param name="Phase">The current work phase, or null when inactive or unknown.</param>
/// <param name="Summary">The work summary, limited to 160 UTF-16 characters.</param>
/// <param name="StartedAtUtc">The earliest retained activity time.</param>
/// <param name="UpdatedAtUtc">The latest retained lifecycle or declaration time.</param>
/// <param name="Evidence">Current state/phase provenance, or null when unknown.</param>
/// <param name="SummaryEvidence">Summary provenance, which can differ from state/phase provenance.</param>
public sealed record WorkspaceActivityAgent(
    string AgentId,
    string AgentType,
    bool IsSubagent,
    AgentWorkState State,
    AgentActivityPhase? Phase,
    string? Summary,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AgentActivityEvidenceKind? Evidence,
    AgentActivityEvidenceKind? SummaryEvidence)
{
    /// <summary>The exact parent session identity for an observed subagent, when known.</summary>
    public string? ParentAgentId { get; init; }

    /// <summary>The last observed work action, excluding terminal lifecycle markers.</summary>
    public string? LastObservedActivity { get; init; }
}
