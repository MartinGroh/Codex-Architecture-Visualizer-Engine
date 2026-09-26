using Cave.Domain;

namespace Cave.Application;

/// <summary>Describes why a semantic entity was included in bounded context.</summary>
public enum SemanticContextConnectionKind
{
    /// <summary>The entity is the requested context root.</summary>
    Root,
    /// <summary>A typed semantic relationship connects the entity.</summary>
    Relationship,
    /// <summary>The entity is a semantic parent.</summary>
    Parent,
    /// <summary>The entity is a semantic child.</summary>
    Child,
    /// <summary>The entity belongs to an expanded child diagram.</summary>
    ChildDiagram,
    /// <summary>The entity owns the diagram containing the current entity.</summary>
    ParentDiagram,
}

/// <summary>Associates one context entity with its shortest semantic distance and connection kinds.</summary>
/// <param name="Entity">The canonical semantic entity.</param>
/// <param name="Depth">The shortest semantic distance from the root.</param>
/// <param name="Connections">The deterministic reasons this entity is relevant.</param>
public sealed record SemanticContextEntity(
    SemanticEntity Entity,
    int Depth,
    IReadOnlyList<SemanticContextConnectionKind> Connections);

/// <summary>Contains bounded semantic context suitable for an adapter or AI operation.</summary>
/// <param name="RootEntityId">The requested semantic root identifier.</param>
/// <param name="RequestedDepth">The accepted traversal depth.</param>
/// <param name="MaximumEntities">The accepted entity bound.</param>
/// <param name="Truncated">Whether additional reachable entities were excluded by the bound.</param>
/// <param name="Entities">The bounded entities in deterministic breadth-first order.</param>
/// <param name="Relationships">Typed relationships whose endpoints are both in context.</param>
/// <param name="Diagrams">Diagrams containing the included entities.</param>
/// <param name="ReferencedAssets">Assets referenced by included image blocks.</param>
public sealed record SemanticEntityContext(
    string RootEntityId,
    int RequestedDepth,
    int MaximumEntities,
    bool Truncated,
    IReadOnlyList<SemanticContextEntity> Entities,
    IReadOnlyList<SemanticRelationship> Relationships,
    IReadOnlyList<SemanticDiagramSummary> Diagrams,
    IReadOnlyList<SemanticAssetReference> ReferencedAssets);
