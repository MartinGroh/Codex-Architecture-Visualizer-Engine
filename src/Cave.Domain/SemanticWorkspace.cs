namespace Cave.Domain;

/// <summary>
/// Owns user- and agent-authored engineering intent without mutating the CodeGraph architecture graph.
/// </summary>
/// <param name="SchemaVersion">The durable schema version.</param>
/// <param name="Id">The stable workspace identifier.</param>
/// <param name="Name">The workspace name.</param>
/// <param name="Revision">The committed semantic revision.</param>
/// <param name="UpdatedAtUtc">The last committed update time.</param>
/// <param name="Diagrams">The semantic diagrams.</param>
/// <param name="Entities">The semantic entities.</param>
/// <param name="Relationships">The typed semantic relationships.</param>
/// <param name="Boards">The configurable Kanban boards.</param>
/// <param name="Assets">The binary asset references.</param>
public sealed record SemanticWorkspace(
    int SchemaVersion,
    string Id,
    string Name,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SemanticDiagram> Diagrams,
    IReadOnlyList<SemanticEntity> Entities,
    IReadOnlyList<SemanticRelationship> Relationships,
    IReadOnlyList<SemanticKanbanBoard> Boards,
    IReadOnlyList<SemanticAssetReference> Assets)
{
    /// <summary>Gets the current durable semantic-workspace schema version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Creates an empty schema-v1 semantic workspace.</summary>
    /// <param name="id">The stable workspace identifier.</param>
    /// <param name="name">The user-facing workspace name.</param>
    /// <param name="now">The authoritative creation time.</param>
    /// <returns>An empty semantic workspace.</returns>
    public static SemanticWorkspace Empty(string id, string name, DateTimeOffset now) => new(
        CurrentSchemaVersion,
        id,
        name,
        0,
        now,
        [],
        [],
        [],
        [],
        []);
}

/// <summary>Defines one semantic diagram and its independent presentation state.</summary>
/// <param name="Id">The stable diagram identifier.</param>
/// <param name="Title">The diagram title.</param>
/// <param name="Purpose">The optional semantic purpose.</param>
/// <param name="ParentDiagramId">The optional parent diagram identifier.</param>
/// <param name="Tracking">The optional tracking facet.</param>
/// <param name="View">The diagram-local presentation state.</param>
/// <param name="Metadata">Extensible semantic metadata.</param>
public sealed record SemanticDiagram(
    string Id,
    string Title,
    string? Purpose,
    string? ParentDiagramId,
    SemanticTrackingFacet? Tracking,
    SemanticDiagramView View,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Defines one editable semantic entity in a diagram.</summary>
/// <param name="Id">The stable entity identifier.</param>
/// <param name="DiagramId">The owning diagram identifier.</param>
/// <param name="Type">The extensible semantic entity type.</param>
/// <param name="Title">The entity title.</param>
/// <param name="Description">The optional description.</param>
/// <param name="ParentEntityId">The optional semantic parent entity.</param>
/// <param name="ChildDiagramId">The optional child diagram expanded by this entity.</param>
/// <param name="ArchitectureNodeId">The optional CodeGraph architecture-node binding.</param>
/// <param name="Blocks">The composable semantic content.</param>
/// <param name="Tracking">The optional tracking facet.</param>
/// <param name="Tags">The semantic tags.</param>
/// <param name="Metadata">Extensible semantic metadata.</param>
public sealed record SemanticEntity(
    string Id,
    string DiagramId,
    string Type,
    string Title,
    string? Description,
    string? ParentEntityId,
    string? ChildDiagramId,
    string? ArchitectureNodeId,
    IReadOnlyList<SemanticContentBlock> Blocks,
    SemanticTrackingFacet? Tracking,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Defines a typed directed relation between two semantic entities.</summary>
/// <param name="Id">The stable relation identifier.</param>
/// <param name="DiagramId">The owning diagram identifier.</param>
/// <param name="SourceEntityId">The source entity identifier.</param>
/// <param name="TargetEntityId">The target entity identifier.</param>
/// <param name="Type">The extensible relationship type.</param>
/// <param name="Label">The optional display label.</param>
/// <param name="Metadata">Extensible semantic metadata.</param>
public sealed record SemanticRelationship(
    string Id,
    string DiagramId,
    string SourceEntityId,
    string TargetEntityId,
    string Type,
    string? Label,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Stores presentation-only state for one semantic diagram.</summary>
/// <param name="Zoom">The optional saved zoom.</param>
/// <param name="ViewportX">The optional viewport X offset.</param>
/// <param name="ViewportY">The optional viewport Y offset.</param>
/// <param name="Entities">Entity presentation entries.</param>
public sealed record SemanticDiagramView(
    double? Zoom,
    double? ViewportX,
    double? ViewportY,
    IReadOnlyList<SemanticEntityPresentation> Entities);

/// <summary>Stores presentation-only state for one entity in its diagram.</summary>
/// <param name="EntityId">The entity identifier.</param>
/// <param name="X">The layout X position.</param>
/// <param name="Y">The layout Y position.</param>
/// <param name="Width">The optional manual width.</param>
/// <param name="Height">The optional manual height.</param>
/// <param name="Collapsed">Whether rich content is collapsed.</param>
/// <param name="ManuallyPositioned">Whether automatic layout must preserve the position.</param>
/// <param name="StyleToken">The optional local presentation token.</param>
public sealed record SemanticEntityPresentation(
    string EntityId,
    double X,
    double Y,
    double? Width,
    double? Height,
    bool Collapsed,
    bool ManuallyPositioned,
    string? StyleToken);

/// <summary>Associates a diagram or entity with a Kanban board and independent progress.</summary>
/// <param name="BoardId">The board identifier.</param>
/// <param name="ColumnId">The current column identifier.</param>
/// <param name="Progress">The progress mode and value.</param>
/// <param name="Metadata">Extensible tracking metadata.</param>
public sealed record SemanticTrackingFacet(
    string BoardId,
    string ColumnId,
    SemanticProgress Progress,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Defines progress independently from Kanban column placement.</summary>
/// <param name="Mode">The progress derivation mode.</param>
/// <param name="Value">The optional percentage value.</param>
public sealed record SemanticProgress(SemanticProgressMode Mode, double? Value);

/// <summary>Specifies how progress is produced.</summary>
public enum SemanticProgressMode
{
    /// <summary>The user or agent sets the percentage directly.</summary>
    Manual,
    /// <summary>Progress is derived from checklist completion.</summary>
    Checklist,
    /// <summary>Progress is derived from tracked children.</summary>
    Children,
}

/// <summary>Defines a configurable Kanban board.</summary>
/// <param name="Id">The stable board identifier.</param>
/// <param name="Title">The board title.</param>
/// <param name="Columns">The ordered user-defined columns.</param>
/// <param name="Metadata">Extensible board metadata.</param>
public sealed record SemanticKanbanBoard(
    string Id,
    string Title,
    IReadOnlyList<SemanticKanbanColumn> Columns,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Defines one ordered Kanban column.</summary>
/// <param name="Id">The stable column identifier.</param>
/// <param name="Title">The column title.</param>
/// <param name="Order">The user-defined order.</param>
/// <param name="ColorToken">The optional visual token.</param>
public sealed record SemanticKanbanColumn(
    string Id,
    string Title,
    int Order,
    string? ColorToken);

/// <summary>References a binary asset without embedding it in the semantic document.</summary>
/// <param name="Id">The stable asset identifier.</param>
/// <param name="RelativePath">The workspace-relative asset path.</param>
/// <param name="MediaType">The media type.</param>
/// <param name="Caption">The optional caption.</param>
/// <param name="AltText">The required accessible description.</param>
/// <param name="Metadata">Extensible asset metadata.</param>
public sealed record SemanticAssetReference(
    string Id,
    string RelativePath,
    string MediaType,
    string? Caption,
    string AltText,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Identifies the actor responsible for a semantic transaction.</summary>
/// <param name="Type">The actor category.</param>
/// <param name="Id">The optional stable actor identifier.</param>
/// <param name="Name">The optional display name.</param>
public sealed record SemanticActor(
    SemanticActorType Type,
    string? Id,
    string? Name);

/// <summary>Classifies semantic-workspace actors without coupling to one agent protocol.</summary>
public enum SemanticActorType
{
    /// <summary>A person operating the application.</summary>
    Human,
    /// <summary>An OpenAI Codex agent.</summary>
    Codex,
    /// <summary>A GitHub Copilot agent.</summary>
    Copilot,
    /// <summary>Another identified agent system.</summary>
    Agent,
    /// <summary>Application automation.</summary>
    System,
}
