using Cave.Domain;
using System.Text.Json.Serialization;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Application;

/// <summary>Defines one typed mutation within an atomic semantic-workspace batch.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "operation")]
[JsonDerivedType(typeof(CreateDiagramOperation), "create-diagram")]
[JsonDerivedType(typeof(UpdateDiagramOperation), "update-diagram")]
[JsonDerivedType(typeof(DeleteDiagramOperation), "delete-diagram")]
[JsonDerivedType(typeof(CreateEntityOperation), "create-entity")]
[JsonDerivedType(typeof(UpdateEntityOperation), "update-entity")]
[JsonDerivedType(typeof(DeleteEntityOperation), "delete-entity")]
[JsonDerivedType(typeof(CreateRelationshipOperation), "create-relationship")]
[JsonDerivedType(typeof(UpdateRelationshipOperation), "update-relationship")]
[JsonDerivedType(typeof(DeleteRelationshipOperation), "delete-relationship")]
[JsonDerivedType(typeof(AddContentBlockOperation), "add-content-block")]
[JsonDerivedType(typeof(UpdateContentBlockOperation), "update-content-block")]
[JsonDerivedType(typeof(DeleteContentBlockOperation), "delete-content-block")]
[JsonDerivedType(typeof(AddChecklistItemOperation), "add-checklist-item")]
[JsonDerivedType(typeof(UpdateChecklistItemOperation), "update-checklist-item")]
[JsonDerivedType(typeof(DeleteChecklistItemOperation), "delete-checklist-item")]
[JsonDerivedType(typeof(ReorderChecklistItemsOperation), "reorder-checklist-items")]
[JsonDerivedType(typeof(CreateBoardOperation), "create-board")]
[JsonDerivedType(typeof(UpdateBoardOperation), "update-board")]
[JsonDerivedType(typeof(DeleteBoardOperation), "delete-board")]
[JsonDerivedType(typeof(AddBoardColumnOperation), "add-board-column")]
[JsonDerivedType(typeof(UpdateBoardColumnOperation), "update-board-column")]
[JsonDerivedType(typeof(DeleteBoardColumnOperation), "delete-board-column")]
[JsonDerivedType(typeof(SetDiagramTrackingOperation), "set-diagram-tracking")]
[JsonDerivedType(typeof(SetEntityTrackingOperation), "set-entity-tracking")]
[JsonDerivedType(typeof(LinkChildDiagramOperation), "link-child-diagram")]
[JsonDerivedType(typeof(UpdateDiagramViewOperation), "update-diagram-view")]
[JsonDerivedType(typeof(CreateAssetReferenceOperation), "create-asset-reference")]
[JsonDerivedType(typeof(UpdateAssetReferenceOperation), "update-asset-reference")]
[JsonDerivedType(typeof(DeleteAssetReferenceOperation), "delete-asset-reference")]
public abstract record SemanticWorkspaceOperation
{
    /// <summary>Gets the stable operation discriminator used in diagnostics and adapters.</summary>
    public abstract string OperationName { get; }

    /// <summary>Gets the primary semantic identifier affected by the operation.</summary>
    public abstract string? TargetId { get; }
}

/// <summary>Creates one diagram; an empty diagram ID requests a generated stable ID.</summary>
/// <param name="Diagram">The diagram to create.</param>
public sealed record CreateDiagramOperation(SemanticDiagram Diagram) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "create-diagram";
    /// <inheritdoc />
    public override string? TargetId => Diagram.Id;
}

/// <summary>Replaces the semantic fields of an existing diagram while preserving its ID.</summary>
/// <param name="Diagram">The replacement diagram.</param>
public sealed record UpdateDiagramOperation(SemanticDiagram Diagram) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-diagram";
    /// <inheritdoc />
    public override string? TargetId => Diagram.Id;
}

/// <summary>Deletes one diagram after the complete batch has removed dependent objects.</summary>
/// <param name="DiagramId">The diagram identifier.</param>
public sealed record DeleteDiagramOperation(string DiagramId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-diagram";
    /// <inheritdoc />
    public override string? TargetId => DiagramId;
}

/// <summary>Creates one entity; an empty entity ID requests a generated stable ID.</summary>
/// <param name="Entity">The entity to create.</param>
public sealed record CreateEntityOperation(SemanticEntity Entity) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "create-entity";
    /// <inheritdoc />
    public override string? TargetId => Entity.Id;
}

/// <summary>Replaces an existing entity while preserving its ID.</summary>
/// <param name="Entity">The replacement entity.</param>
public sealed record UpdateEntityOperation(SemanticEntity Entity) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-entity";
    /// <inheritdoc />
    public override string? TargetId => Entity.Id;
}

/// <summary>Deletes one entity after the complete batch has removed its dependants.</summary>
/// <param name="EntityId">The entity identifier.</param>
public sealed record DeleteEntityOperation(string EntityId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-entity";
    /// <inheritdoc />
    public override string? TargetId => EntityId;
}

/// <summary>Creates one typed relationship; an empty ID requests a generated stable ID.</summary>
/// <param name="Relationship">The relationship to create.</param>
public sealed record CreateRelationshipOperation(SemanticRelationship Relationship) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "create-relationship";
    /// <inheritdoc />
    public override string? TargetId => Relationship.Id;
}

/// <summary>Replaces an existing typed relationship.</summary>
/// <param name="Relationship">The replacement relationship.</param>
public sealed record UpdateRelationshipOperation(SemanticRelationship Relationship) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-relationship";
    /// <inheritdoc />
    public override string? TargetId => Relationship.Id;
}

/// <summary>Deletes one typed relationship.</summary>
/// <param name="RelationshipId">The relationship identifier.</param>
public sealed record DeleteRelationshipOperation(string RelationshipId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-relationship";
    /// <inheritdoc />
    public override string? TargetId => RelationshipId;
}

/// <summary>Adds a rich-content block to an entity; an empty block ID requests a generated ID.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="Block">The block to append.</param>
public sealed record AddContentBlockOperation(string EntityId, SemanticContentBlock Block) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "add-content-block";
    /// <inheritdoc />
    public override string? TargetId => Block.Id;
}

/// <summary>Replaces one rich-content block on an entity.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="Block">The replacement block.</param>
public sealed record UpdateContentBlockOperation(string EntityId, SemanticContentBlock Block) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-content-block";
    /// <inheritdoc />
    public override string? TargetId => Block.Id;
}

/// <summary>Deletes one rich-content block from an entity.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="BlockId">The block identifier.</param>
public sealed record DeleteContentBlockOperation(string EntityId, string BlockId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-content-block";
    /// <inheritdoc />
    public override string? TargetId => BlockId;
}

/// <summary>Adds a stable item to a checklist block; an empty item ID requests a generated ID.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="BlockId">The checklist block identifier.</param>
/// <param name="Item">The item to add.</param>
public sealed record AddChecklistItemOperation(
    string EntityId,
    string BlockId,
    SemanticChecklistItem Item) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "add-checklist-item";
    /// <inheritdoc />
    public override string? TargetId => Item.Id;
}

/// <summary>Replaces one stable checklist item.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="BlockId">The checklist block identifier.</param>
/// <param name="Item">The replacement item.</param>
public sealed record UpdateChecklistItemOperation(
    string EntityId,
    string BlockId,
    SemanticChecklistItem Item) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-checklist-item";
    /// <inheritdoc />
    public override string? TargetId => Item.Id;
}

/// <summary>Deletes one checklist item.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="BlockId">The checklist block identifier.</param>
/// <param name="ItemId">The item identifier.</param>
public sealed record DeleteChecklistItemOperation(
    string EntityId,
    string BlockId,
    string ItemId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-checklist-item";
    /// <inheritdoc />
    public override string? TargetId => ItemId;
}

/// <summary>Reorders every item in one checklist using stable item IDs.</summary>
/// <param name="EntityId">The owning entity identifier.</param>
/// <param name="BlockId">The checklist block identifier.</param>
/// <param name="ItemIds">Every existing item ID in desired order.</param>
public sealed record ReorderChecklistItemsOperation(
    string EntityId,
    string BlockId,
    IReadOnlyList<string> ItemIds) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "reorder-checklist-items";
    /// <inheritdoc />
    public override string? TargetId => BlockId;
}

/// <summary>Creates one Kanban board; an empty board ID requests a generated stable ID.</summary>
/// <param name="Board">The board to create.</param>
public sealed record CreateBoardOperation(SemanticKanbanBoard Board) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "create-board";
    /// <inheritdoc />
    public override string? TargetId => Board.Id;
}

/// <summary>Replaces one existing Kanban board.</summary>
/// <param name="Board">The replacement board.</param>
public sealed record UpdateBoardOperation(SemanticKanbanBoard Board) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-board";
    /// <inheritdoc />
    public override string? TargetId => Board.Id;
}

/// <summary>Deletes a Kanban board after the complete batch has untracked its cards.</summary>
/// <param name="BoardId">The board identifier.</param>
public sealed record DeleteBoardOperation(string BoardId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-board";
    /// <inheritdoc />
    public override string? TargetId => BoardId;
}

/// <summary>Adds a user-configurable column to an existing board.</summary>
/// <param name="BoardId">The board identifier.</param>
/// <param name="Column">The column to add.</param>
public sealed record AddBoardColumnOperation(
    string BoardId,
    SemanticKanbanColumn Column) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "add-board-column";
    /// <inheritdoc />
    public override string? TargetId => Column.Id;
}

/// <summary>Replaces one user-configurable board column.</summary>
/// <param name="BoardId">The board identifier.</param>
/// <param name="Column">The replacement column.</param>
public sealed record UpdateBoardColumnOperation(
    string BoardId,
    SemanticKanbanColumn Column) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-board-column";
    /// <inheritdoc />
    public override string? TargetId => Column.Id;
}

/// <summary>Deletes one board column after the complete batch has moved or untracked its cards.</summary>
/// <param name="BoardId">The board identifier.</param>
/// <param name="ColumnId">The column identifier.</param>
public sealed record DeleteBoardColumnOperation(string BoardId, string ColumnId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-board-column";
    /// <inheritdoc />
    public override string? TargetId => ColumnId;
}

/// <summary>Tracks or untracks a diagram without creating a duplicate Kanban card.</summary>
/// <param name="DiagramId">The source diagram identifier.</param>
/// <param name="Tracking">The tracking facet, or <see langword="null"/> to untrack.</param>
public sealed record SetDiagramTrackingOperation(
    string DiagramId,
    SemanticTrackingFacet? Tracking) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "set-diagram-tracking";
    /// <inheritdoc />
    public override string? TargetId => DiagramId;
}

/// <summary>Tracks or untracks an entity without creating a duplicate Kanban card.</summary>
/// <param name="EntityId">The source entity identifier.</param>
/// <param name="Tracking">The tracking facet, or <see langword="null"/> to untrack.</param>
public sealed record SetEntityTrackingOperation(
    string EntityId,
    SemanticTrackingFacet? Tracking) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "set-entity-tracking";
    /// <inheritdoc />
    public override string? TargetId => EntityId;
}

/// <summary>Links or unlinks an entity's child diagram.</summary>
/// <param name="EntityId">The source entity identifier.</param>
/// <param name="ChildDiagramId">The child diagram identifier, or <see langword="null"/> to unlink.</param>
public sealed record LinkChildDiagramOperation(
    string EntityId,
    string? ChildDiagramId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "link-child-diagram";
    /// <inheritdoc />
    public override string? TargetId => EntityId;
}

/// <summary>Replaces presentation-only state for one semantic diagram.</summary>
/// <param name="DiagramId">The diagram identifier.</param>
/// <param name="View">The replacement presentation state.</param>
public sealed record UpdateDiagramViewOperation(
    string DiagramId,
    SemanticDiagramView View) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-diagram-view";
    /// <inheritdoc />
    public override string? TargetId => DiagramId;
}

/// <summary>Creates an image or other binary asset reference without uploading bytes.</summary>
/// <param name="Asset">The asset reference to create.</param>
public sealed record CreateAssetReferenceOperation(SemanticAssetReference Asset) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "create-asset-reference";
    /// <inheritdoc />
    public override string? TargetId => Asset.Id;
}

/// <summary>Replaces an existing binary asset reference.</summary>
/// <param name="Asset">The replacement asset reference.</param>
public sealed record UpdateAssetReferenceOperation(SemanticAssetReference Asset) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "update-asset-reference";
    /// <inheritdoc />
    public override string? TargetId => Asset.Id;
}

/// <summary>Deletes an asset reference after the complete batch has removed image-block references.</summary>
/// <param name="AssetId">The asset identifier.</param>
public sealed record DeleteAssetReferenceOperation(string AssetId) : SemanticWorkspaceOperation
{
    /// <inheritdoc />
    public override string OperationName => "delete-asset-reference";
    /// <inheritdoc />
    public override string? TargetId => AssetId;
}

/// <summary>Describes one atomic set of semantic-workspace operations.</summary>
/// <param name="Actor">The human or agent responsible for the batch.</param>
/// <param name="Summary">A concise description of the batch intent.</param>
/// <param name="Operations">The ordered typed operations to validate and commit together.</param>
public sealed record SemanticWorkspaceBatch(
    SemanticActor Actor,
    string Summary,
    IReadOnlyList<SemanticWorkspaceOperation> Operations);

/// <summary>Provides a stable, adapter-friendly semantic validation diagnostic.</summary>
/// <param name="Code">The stable machine-readable error code.</param>
/// <param name="Message">The actionable human-readable diagnostic.</param>
/// <param name="OperationIndex">The zero-based operation index when attributable.</param>
/// <param name="TargetId">The semantic identifier associated with the failure.</param>
/// <param name="Alternatives">Useful valid identifiers or corrective alternatives.</param>
public sealed record SemanticWorkspaceError(
    string Code,
    string Message,
    int? OperationIndex,
    string? TargetId,
    IReadOnlyList<string> Alternatives);

/// <summary>Records provenance for one committed semantic-workspace transaction.</summary>
/// <param name="Id">The stable transaction identifier.</param>
/// <param name="Actor">The actor responsible for the transaction.</param>
/// <param name="OccurredAtUtc">The authoritative commit time.</param>
/// <param name="Summary">The concise transaction summary.</param>
/// <param name="AffectedIds">The stable semantic identifiers affected by the batch.</param>
/// <param name="OperationCount">The number of typed operations committed together.</param>
public sealed record SemanticWorkspaceTransaction(
    string Id,
    SemanticActor Actor,
    DateTimeOffset OccurredAtUtc,
    string Summary,
    IReadOnlyList<string> AffectedIds,
    int OperationCount);

/// <summary>Carries one semantic batch, undo, or redo outcome without partial commits.</summary>
/// <param name="Succeeded">Whether a complete durable commit occurred.</param>
/// <param name="Workspace">The committed workspace, or the unchanged current workspace after failure.</param>
/// <param name="CreatedIds">IDs generated or accepted by successful create operations.</param>
/// <param name="Errors">Structured errors for a rejected operation or aggregate.</param>
/// <param name="Transaction">Provenance for the committed transaction.</param>
public sealed record SemanticWorkspaceOperationResult(
    bool Succeeded,
    SemanticWorkspaceModel Workspace,
    IReadOnlyList<string> CreatedIds,
    IReadOnlyList<SemanticWorkspaceError> Errors,
    SemanticWorkspaceTransaction? Transaction);
