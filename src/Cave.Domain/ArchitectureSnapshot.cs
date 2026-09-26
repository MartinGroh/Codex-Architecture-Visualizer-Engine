namespace Cave.Domain;

/// <summary>
/// Describes the provenance and freshness of a projected architecture snapshot.
/// </summary>
/// <param name="WorkspaceName">The displayed workspace name.</param>
/// <param name="ProviderId">The configured provider identity.</param>
/// <param name="SourceKind">The kind of source that produced the graph.</param>
/// <param name="IsLive">Whether the source reflects live workspace data.</param>
/// <param name="GeneratedAtUtc">The time the snapshot was produced.</param>
public sealed record SnapshotMetadata(
    string WorkspaceName,
    string ProviderId,
    SnapshotSourceKind SourceKind,
    bool IsLive,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Carries a normalized graph and explicit provenance to presentation clients.
/// </summary>
/// <param name="Metadata">The snapshot provenance and freshness.</param>
/// <param name="Graph">The validated architecture graph.</param>
/// <param name="Git">The independent Git delta overlay.</param>
/// <param name="Activity">The independent observed-activity and declared-scope overlay.</param>
/// <param name="Conversation">The independent opt-in public conversation overlay.</param>
public sealed record ArchitectureSnapshot(
    SnapshotMetadata Metadata,
    ArchitectureGraph Graph,
    GitDeltaOverlay Git,
    AgentActivityOverlay Activity,
    ConversationOverlay Conversation);
