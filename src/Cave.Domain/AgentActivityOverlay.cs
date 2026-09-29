namespace Cave.Domain;

/// <summary>
/// Describes the lifecycle state of one observed or declared agent.
/// </summary>
public enum AgentWorkState
{
    /// <summary>The agent has declared future work but has not started it.</summary>
    Planned,

    /// <summary>The agent is currently working.</summary>
    Active,

    /// <summary>The session remains known but is not executing a turn.</summary>
    Idle,

    /// <summary>The agent or session has finished its declared work.</summary>
    Completed,
}

/// <summary>
/// Describes the current evidence-backed phase of active agent work.
/// </summary>
public enum AgentActivityPhase
{
    /// <summary>Codex has received an instruction or started an agent and is processing it.</summary>
    Thinking,

    /// <summary>The latest observed tool activity reads, searches, or inspects information.</summary>
    Reading,

    /// <summary>The latest observed tool activity changes workspace state.</summary>
    Editing,

    /// <summary>The latest observed tool activity builds, tests, lints, or verifies the workspace.</summary>
    Validating,

    /// <summary>The agent is active but the observed activity has no more specific safe classification.</summary>
    Working,
}

/// <summary>
/// Identifies whether node activity was observed from a Codex hook or explicitly declared by an agent.
/// </summary>
public enum AgentActivityEvidenceKind
{
    /// <summary>The activity was objectively observed from a Codex lifecycle or tool hook.</summary>
    Observed,

    /// <summary>The scope was explicitly declared by an agent through CAVE.</summary>
    Declared,
}

/// <summary>
/// Describes whether the repository-local Codex activity bridge has produced readable evidence.
/// </summary>
public enum AgentActivitySourceStatus
{
    /// <summary>No valid hook event has been observed in this workspace yet.</summary>
    Unobserved,

    /// <summary>The activity journal contains readable Codex or declared-scope events.</summary>
    Ready,

    /// <summary>Some activity evidence was readable, but one or more journal records failed validation.</summary>
    Degraded,
}

/// <summary>
/// Describes one main agent or subagent currently known to CAVE.
/// </summary>
/// <param name="AgentId">The stable session or subagent identifier.</param>
/// <param name="AgentType">The Codex agent type or a declared role label.</param>
/// <param name="IsSubagent">Whether the identity represents a child agent.</param>
/// <param name="State">The latest combined lifecycle state.</param>
/// <param name="Phase">The current observed or declared phase while active, or <see langword="null"/> otherwise.</param>
/// <param name="Summary">The latest objective activity or declared work summary.</param>
/// <param name="HasObservedActivity">Whether Codex hooks objectively observed this agent.</param>
/// <param name="HasDeclaredScope">Whether the agent explicitly declared architecture scope.</param>
/// <param name="StartedAtUtc">The earliest retained activity time.</param>
/// <param name="UpdatedAtUtc">The latest retained activity time.</param>
public sealed record AgentActivity(
    string AgentId,
    string AgentType,
    bool IsSubagent,
    AgentWorkState State,
    AgentActivityPhase? Phase,
    string? Summary,
    bool HasObservedActivity,
    bool HasDeclaredScope,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>
    /// Gets the application-supplied presentation name, or null before presentation enrichment.
    /// The stable agent identity remains authoritative; names may repeat.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the provenance of the event supplying current lifecycle state and phase, or null when unknown.
    /// Historical observed/declared flags do not establish this provenance.
    /// </summary>
    public AgentActivityEvidenceKind? Evidence { get; init; }

    /// <summary>
    /// Gets the provenance of the displayed work summary, which can differ from the latest lifecycle event.
    /// </summary>
    public AgentActivityEvidenceKind? SummaryEvidence { get; init; }

    /// <summary>The exact observed parent session identity for a subagent, when known.</summary>
    public string? ParentAgentId { get; init; }

    /// <summary>The last meaningful observed work action, excluding lifecycle-only hooks.</summary>
    public string? LastObservedActivity { get; init; }

    /// <summary>A privacy-preserving external correlation key supplied by Application.</summary>
    public string? PublicId { get; init; }
}

/// <summary>
/// Associates an agent with one architecture node using explicit evidence semantics.
/// </summary>
/// <param name="AgentId">The related agent identifier.</param>
/// <param name="NodeId">The exact architecture node identifier.</param>
/// <param name="Evidence">Whether the association was observed or declared.</param>
/// <param name="IsDirect">Whether the evidence maps directly to this node rather than a projected ancestor.</param>
/// <param name="UpdatedAtUtc">The latest evidence time.</param>
/// <param name="Paths">Workspace-relative files supporting the association.</param>
public sealed record AgentNodeActivity(
    string AgentId,
    string NodeId,
    AgentActivityEvidenceKind Evidence,
    bool IsDirect,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> Paths);

/// <summary>
/// Records a recent objectively observed file edit and its semantic mapping.
/// </summary>
/// <param name="AgentId">The agent associated with the edit.</param>
/// <param name="FilePath">The workspace-relative edited file.</param>
/// <param name="NodeIds">The semantic nodes declared in that file.</param>
/// <param name="ObservedAtUtc">The hook observation time.</param>
public sealed record RecentAgentEdit(
    string AgentId,
    string FilePath,
    IReadOnlyList<string> NodeIds,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Identifies the latest user instruction without retaining prompt content.
/// </summary>
/// <param name="Id">A stable session-and-turn identity for the instruction.</param>
/// <param name="ObservedAtUtc">The time Codex submitted the instruction.</param>
public sealed record AgentInstructionMarker(
    string Id,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Carries observed Codex activity and agent-declared scope independently from static graph and Git evidence.
/// </summary>
/// <param name="Agents">Known main agents and subagents.</param>
/// <param name="Nodes">Evidence-backed associations between agents and architecture nodes.</param>
/// <param name="RecentEdits">Recent objective edit observations.</param>
/// <param name="LatestInstruction">The latest observed instruction boundary, without prompt content.</param>
/// <param name="UnmappedPaths">Observed or declared files that could not be mapped to a semantic node.</param>
/// <param name="Error">A journal read error, or <see langword="null"/> when the overlay is current.</param>
public sealed record AgentActivityOverlay(
    IReadOnlyList<AgentActivity> Agents,
    IReadOnlyList<AgentNodeActivity> Nodes,
    IReadOnlyList<RecentAgentEdit> RecentEdits,
    AgentInstructionMarker? LatestInstruction,
    IReadOnlyList<string> UnmappedPaths,
    string? Error)
{
    /// <summary>
    /// Gets the health of the repository-local activity evidence source.
    /// </summary>
    public AgentActivitySourceStatus SourceStatus { get; init; } = AgentActivitySourceStatus.Unobserved;

    /// <summary>
    /// Gets an empty, current overlay for workspaces without activity evidence.
    /// </summary>
    public static AgentActivityOverlay Empty { get; } = new([], [], [], null, [], null);
}
