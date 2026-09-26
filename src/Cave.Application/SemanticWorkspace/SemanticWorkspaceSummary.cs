using Cave.Domain;

namespace Cave.Application;

/// <summary>Summarizes a semantic workspace without presentation state or duplicated task cards.</summary>
/// <param name="SchemaVersion">The durable semantic schema version.</param>
/// <param name="Id">The stable workspace identifier.</param>
/// <param name="Name">The workspace name.</param>
/// <param name="Revision">The committed semantic revision.</param>
/// <param name="UpdatedAtUtc">The last committed update time.</param>
/// <param name="DiagramCount">The total diagram count.</param>
/// <param name="EntityCount">The total semantic entity count.</param>
/// <param name="RelationshipCount">The total typed relationship count.</param>
/// <param name="BoardCount">The total tracking-board count.</param>
/// <param name="AssetCount">The total asset-reference count.</param>
/// <param name="TrackedDiagramCount">The number of diagrams with a tracking facet.</param>
/// <param name="TrackedEntityCount">The number of entities with a tracking facet.</param>
/// <param name="Diagrams">Deterministic per-diagram summaries.</param>
public sealed record SemanticWorkspaceSummary(
    int SchemaVersion,
    string Id,
    string Name,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    int DiagramCount,
    int EntityCount,
    int RelationshipCount,
    int BoardCount,
    int AssetCount,
    int TrackedDiagramCount,
    int TrackedEntityCount,
    IReadOnlyList<SemanticDiagramSummary> Diagrams);

/// <summary>Summarizes one semantic diagram using canonical stable identifiers.</summary>
/// <param name="Id">The stable diagram identifier.</param>
/// <param name="Title">The diagram title.</param>
/// <param name="Purpose">The optional semantic purpose.</param>
/// <param name="ParentDiagramId">The optional parent diagram identifier.</param>
/// <param name="EntityCount">The number of entities owned by the diagram.</param>
/// <param name="RelationshipCount">The number of typed relationships owned by the diagram.</param>
/// <param name="ChildDiagramCount">The number of direct semantic child diagrams.</param>
/// <param name="ContentBlockCount">The total number of rich-content blocks on the diagram entities.</param>
/// <param name="TrackedEntityCount">The number of diagram entities with tracking facets.</param>
/// <param name="Tracking">The diagram tracking facet, when present.</param>
/// <param name="Metadata">The diagram semantic metadata.</param>
public sealed record SemanticDiagramSummary(
    string Id,
    string Title,
    string? Purpose,
    string? ParentDiagramId,
    int EntityCount,
    int RelationshipCount,
    int ChildDiagramCount,
    int ContentBlockCount,
    int TrackedEntityCount,
    SemanticTrackingFacet? Tracking,
    IReadOnlyDictionary<string, string> Metadata);
