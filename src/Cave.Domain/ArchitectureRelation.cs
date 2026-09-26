namespace Cave.Domain;

/// <summary>
/// Represents a directed semantic relation between two architecture nodes.
/// </summary>
/// <param name="Id">The stable relation identifier.</param>
/// <param name="SourceId">The consumer or derived endpoint.</param>
/// <param name="TargetId">The provider or base endpoint.</param>
/// <param name="Kind">The relation semantics.</param>
/// <param name="Weight">The aggregate visual weight.</param>
/// <param name="EvidenceCount">The number of detailed facts represented by this relation.</param>
/// <param name="Confidence">The evidence confidence.</param>
public sealed record ArchitectureRelation(
    string Id,
    string SourceId,
    string TargetId,
    ArchitectureRelationKind Kind,
    int Weight,
    int EvidenceCount,
    EvidenceConfidence Confidence);
