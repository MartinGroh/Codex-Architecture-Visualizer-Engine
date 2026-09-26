using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Provides the complete canonical state required to render or inspect one semantic diagram.
/// </summary>
/// <param name="Diagram">The requested diagram including its independent presentation state.</param>
/// <param name="Entities">All entities owned by the diagram.</param>
/// <param name="Relationships">All typed relationships owned by the diagram.</param>
/// <param name="ChildDiagrams">Direct child diagrams reached by hierarchy or entity expansion.</param>
/// <param name="ReferencedAssets">Assets referenced by image blocks in the diagram.</param>
/// <param name="TrackingBoards">Boards referenced by the diagram or its entities.</param>
public sealed record SemanticDiagramReadModel(
    SemanticDiagram Diagram,
    IReadOnlyList<SemanticEntity> Entities,
    IReadOnlyList<SemanticRelationship> Relationships,
    IReadOnlyList<SemanticDiagramSummary> ChildDiagrams,
    IReadOnlyList<SemanticAssetReference> ReferencedAssets,
    IReadOnlyList<SemanticKanbanBoard> TrackingBoards);
