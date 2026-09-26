using System.Text.RegularExpressions;
using System.Globalization;
using Cave.Domain;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Application;

/// <summary>
/// Centralizes schema-v1 semantic-workspace invariants for every HTTP, MCP, and persistence caller.
/// </summary>
public static partial class SemanticWorkspaceValidator
{
    /// <summary>Validates the complete aggregate independently from any presentation adapter.</summary>
    /// <param name="workspace">The aggregate to validate.</param>
    /// <returns>Every discovered invariant violation.</returns>
    public static IReadOnlyList<SemanticWorkspaceError> Validate(SemanticWorkspaceModel workspace) =>
        Validate(workspace, new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>
    /// Validates the complete aggregate and attributes errors to the latest operation affecting a target.
    /// </summary>
    /// <param name="workspace">The aggregate to validate.</param>
    /// <param name="operationByTarget">Latest zero-based operation index per affected stable ID.</param>
    /// <returns>Every discovered invariant violation.</returns>
    public static IReadOnlyList<SemanticWorkspaceError> Validate(
        SemanticWorkspaceModel workspace,
        IReadOnlyDictionary<string, int> operationByTarget)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(operationByTarget);

        var errors = new List<SemanticWorkspaceError>();
        ValidateStructure(workspace, errors, operationByTarget);
        if (errors.Count > 0)
        {
            return errors;
        }

        if (workspace.SchemaVersion != SemanticWorkspaceModel.CurrentSchemaVersion)
        {
            Add(
                errors,
                operationByTarget,
                "unsupported_schema",
                $"Schema version {workspace.SchemaVersion} is unsupported.",
                workspace.Id,
                [SemanticWorkspaceModel.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)]);
        }

        RequireId(errors, operationByTarget, workspace.Id, "workspace", workspace.Id);
        RequireText(errors, operationByTarget, workspace.Name, "workspace_name_required", "Workspace name is required.", workspace.Id);

        var diagrams = workspace.Diagrams ?? [];
        var entities = workspace.Entities ?? [];
        var relationships = workspace.Relationships ?? [];
        var boards = workspace.Boards ?? [];
        var assets = workspace.Assets ?? [];

        ValidateUniqueIds(errors, operationByTarget, EnumerateStableIds(workspace), "workspace object");

        var diagramById = diagrams
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var entityById = entities
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var boardById = boards
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var assetIds = assets
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var diagram in diagrams)
        {
            RequireId(errors, operationByTarget, diagram.Id, "diagram", diagram.Id);
            RequireText(errors, operationByTarget, diagram.Title, "diagram_title_required", "Diagram title is required.", diagram.Id);
            ValidateMetadata(errors, operationByTarget, diagram.Metadata, diagram.Id);
            if (diagram.ParentDiagramId is not null && !diagramById.ContainsKey(diagram.ParentDiagramId))
            {
                Add(errors, operationByTarget, "diagram_parent_missing", $"Diagram '{diagram.Id}' references unknown parent '{diagram.ParentDiagramId}'.", diagram.ParentDiagramId, diagramById.Keys, diagram.Id);
            }

            ValidateTracking(errors, operationByTarget, diagram.Tracking, diagram.Id, boardById);
            ValidateView(errors, operationByTarget, diagram, entityById);
        }

        ValidatePointerCycles(
            errors,
            operationByTarget,
            diagrams.Select(item => item.Id),
            id => diagramById.GetValueOrDefault(id)?.ParentDiagramId,
            "diagram_parent_cycle",
            "Diagram parent hierarchy contains a cycle.");

        foreach (var entity in entities)
        {
            RequireId(errors, operationByTarget, entity.Id, "entity", entity.Id);
            RequireText(errors, operationByTarget, entity.Title, "entity_title_required", "Entity title is required.", entity.Id);
            RequireToken(errors, operationByTarget, entity.Type, "entity_type_invalid", entity.Id);
            if (!diagramById.ContainsKey(entity.DiagramId))
            {
                Add(errors, operationByTarget, "entity_diagram_missing", $"Entity '{entity.Id}' references unknown diagram '{entity.DiagramId}'.", entity.DiagramId, diagramById.Keys, entity.Id);
            }

            if (entity.ParentEntityId is not null)
            {
                if (!entityById.TryGetValue(entity.ParentEntityId, out var parent))
                {
                    Add(errors, operationByTarget, "entity_parent_missing", $"Entity '{entity.Id}' references unknown parent '{entity.ParentEntityId}'.", entity.ParentEntityId, entityById.Keys, entity.Id);
                }
                else if (!parent.DiagramId.Equals(entity.DiagramId, StringComparison.Ordinal))
                {
                    Add(errors, operationByTarget, "entity_parent_wrong_diagram", $"Entity '{entity.Id}' and parent '{parent.Id}' must belong to the same diagram.", parent.Id, [entity.DiagramId], entity.Id);
                }
            }

            if (entity.ChildDiagramId is not null && !diagramById.ContainsKey(entity.ChildDiagramId))
            {
                Add(errors, operationByTarget, "child_diagram_missing", $"Entity '{entity.Id}' references unknown child diagram '{entity.ChildDiagramId}'.", entity.ChildDiagramId, diagramById.Keys, entity.Id);
            }

            ValidateTracking(errors, operationByTarget, entity.Tracking, entity.Id, boardById);
            ValidateBlocks(errors, operationByTarget, entity, assetIds);
            ValidateMetadata(errors, operationByTarget, entity.Metadata, entity.Id);
        }

        ValidatePointerCycles(
            errors,
            operationByTarget,
            entities.Select(item => item.Id),
            id => entityById.GetValueOrDefault(id)?.ParentEntityId,
            "entity_parent_cycle",
            "Entity parent hierarchy contains a cycle.");
        ValidateDiagramCycles(errors, operationByTarget, diagrams, entities, diagramById);

        foreach (var relationship in relationships)
        {
            RequireId(errors, operationByTarget, relationship.Id, "relationship", relationship.Id);
            RequireToken(errors, operationByTarget, relationship.Type, "relationship_type_invalid", relationship.Id);
            if (!diagramById.ContainsKey(relationship.DiagramId))
            {
                Add(errors, operationByTarget, "relationship_diagram_missing", $"Relationship '{relationship.Id}' references unknown diagram '{relationship.DiagramId}'.", relationship.DiagramId, diagramById.Keys, relationship.Id);
            }

            ValidateRelationshipEndpoint(errors, operationByTarget, relationship, relationship.SourceEntityId, "source", entityById);
            ValidateRelationshipEndpoint(errors, operationByTarget, relationship, relationship.TargetEntityId, "target", entityById);
            ValidateMetadata(errors, operationByTarget, relationship.Metadata, relationship.Id);
        }

        foreach (var board in boards)
        {
            RequireId(errors, operationByTarget, board.Id, "board", board.Id);
            RequireText(errors, operationByTarget, board.Title, "board_title_required", "Board title is required.", board.Id);
            ValidateMetadata(errors, operationByTarget, board.Metadata, board.Id);
            var columns = board.Columns ?? [];
            ValidateUniqueOrders(errors, operationByTarget, columns.Select(item => (item.Id, item.Order)), "board_column_order_duplicate");
            foreach (var column in columns)
            {
                RequireId(errors, operationByTarget, column.Id, "column", column.Id, board.Id);
                RequireText(errors, operationByTarget, column.Title, "column_title_required", "Board column title is required.", column.Id, board.Id);
                if (column.Order < 0)
                {
                    Add(errors, operationByTarget, "column_order_invalid", $"Column '{column.Id}' has a negative order.", column.Id, ["0 or greater"], board.Id);
                }
            }
        }

        foreach (var asset in assets)
        {
            RequireId(errors, operationByTarget, asset.Id, "asset", asset.Id);
            RequireText(errors, operationByTarget, asset.MediaType, "asset_media_type_required", "Asset media type is required.", asset.Id);
            RequireText(errors, operationByTarget, asset.AltText, "asset_alt_text_required", "Asset alt text is required.", asset.Id);
            if (string.IsNullOrWhiteSpace(asset.RelativePath)
                || Path.IsPathFullyQualified(asset.RelativePath)
                || asset.RelativePath.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal))
            {
                Add(errors, operationByTarget, "asset_path_invalid", $"Asset '{asset.Id}' must use a workspace-relative path without parent traversal.", asset.Id, ["assets/<file>" ]);
            }

            ValidateMetadata(errors, operationByTarget, asset.Metadata, asset.Id);
        }

        return errors;
    }

    /// <summary>Rejects malformed required operation payloads before normalization or mutation.</summary>
    /// <param name="operation">The caller-supplied typed operation.</param>
    /// <returns>Structured errors for missing objects, collections, or collection entries.</returns>
    public static IReadOnlyList<SemanticWorkspaceError> ValidateOperationStructure(SemanticWorkspaceOperation operation)
    {
        var errors = new List<SemanticWorkspaceError>();
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        switch (operation)
        {
            case CreateDiagramOperation value: ValidateDiagramStructure(value.Diagram, errors, targets); break;
            case UpdateDiagramOperation value: ValidateDiagramStructure(value.Diagram, errors, targets); break;
            case CreateEntityOperation value: ValidateEntityStructure(value.Entity, errors, targets); break;
            case UpdateEntityOperation value: ValidateEntityStructure(value.Entity, errors, targets); break;
            case CreateBoardOperation value: ValidateBoardStructure(value.Board, errors, targets); break;
            case UpdateBoardOperation value: ValidateBoardStructure(value.Board, errors, targets); break;
            case AddContentBlockOperation value: ValidateBlockStructure(value.Block, errors, targets, value.EntityId); break;
            case UpdateContentBlockOperation value: ValidateBlockStructure(value.Block, errors, targets, value.EntityId); break;
            case UpdateDiagramViewOperation value: ValidateViewStructure(value.View, errors, targets, value.DiagramId); break;
            case SetEntityTrackingOperation value: ValidateTrackingStructure(value.Tracking, errors, targets, value.EntityId); break;
            case SetDiagramTrackingOperation value: ValidateTrackingStructure(value.Tracking, errors, targets, value.DiagramId); break;
            case CreateRelationshipOperation value: RequireObject(value.Relationship, "relationship", null, errors, targets); break;
            case UpdateRelationshipOperation value: RequireObject(value.Relationship, "relationship", null, errors, targets); break;
            case AddChecklistItemOperation value: RequireObject(value.Item, "checklist item", value.BlockId, errors, targets); break;
            case UpdateChecklistItemOperation value: RequireObject(value.Item, "checklist item", value.BlockId, errors, targets); break;
            case ReorderChecklistItemsOperation value: RequireCollection(value.ItemIds, "checklist item ids", value.BlockId, errors, targets); break;
            case AddBoardColumnOperation value: RequireObject(value.Column, "board column", value.BoardId, errors, targets); break;
            case UpdateBoardColumnOperation value: RequireObject(value.Column, "board column", value.BoardId, errors, targets); break;
            case CreateAssetReferenceOperation value: RequireObject(value.Asset, "asset", null, errors, targets); break;
            case UpdateAssetReferenceOperation value: RequireObject(value.Asset, "asset", null, errors, targets); break;
        }

        return errors;
    }

    private static void ValidateStructure(
        SemanticWorkspaceModel workspace,
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> targets)
    {
        RequireCollection(workspace.Diagrams, "diagrams", workspace.Id, errors, targets);
        RequireCollection(workspace.Entities, "entities", workspace.Id, errors, targets);
        RequireCollection(workspace.Relationships, "relationships", workspace.Id, errors, targets);
        RequireCollection(workspace.Boards, "boards", workspace.Id, errors, targets);
        RequireCollection(workspace.Assets, "assets", workspace.Id, errors, targets);
        foreach (var diagram in workspace.Diagrams ?? []) ValidateDiagramStructure(diagram, errors, targets);
        foreach (var entity in workspace.Entities ?? []) ValidateEntityStructure(entity, errors, targets);
        foreach (var board in workspace.Boards ?? []) ValidateBoardStructure(board, errors, targets);
    }

    private static void ValidateDiagramStructure(SemanticDiagram? diagram, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets)
    {
        if (!RequireObject(diagram, "diagram", null, errors, targets)) return;
        ValidateViewStructure(diagram!.View, errors, targets, diagram.Id);
        ValidateTrackingStructure(diagram.Tracking, errors, targets, diagram.Id);
    }

    private static void ValidateEntityStructure(SemanticEntity? entity, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets)
    {
        if (!RequireObject(entity, "entity", null, errors, targets)) return;
        RequireCollection(entity!.Blocks, "entity blocks", entity.Id, errors, targets);
        RequireCollection(entity.Tags, "entity tags", entity.Id, errors, targets);
        ValidateTrackingStructure(entity.Tracking, errors, targets, entity.Id);
        foreach (var block in entity.Blocks ?? []) ValidateBlockStructure(block, errors, targets, entity.Id);
    }

    private static void ValidateBoardStructure(SemanticKanbanBoard? board, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets)
    {
        if (!RequireObject(board, "board", null, errors, targets)) return;
        RequireCollection(board!.Columns, "board columns", board.Id, errors, targets);
    }

    private static void ValidateViewStructure(SemanticDiagramView? view, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets, string ownerId)
    {
        if (!RequireObject(view, "diagram view", ownerId, errors, targets)) return;
        RequireCollection(view!.Entities, "view entries", ownerId, errors, targets);
    }

    private static void ValidateTrackingStructure(SemanticTrackingFacet? tracking, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets, string ownerId)
    {
        if (tracking is not null) RequireObject(tracking.Progress, "tracking progress", ownerId, errors, targets);
    }

    private static void ValidateBlockStructure(SemanticContentBlock? block, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets, string ownerId)
    {
        if (!RequireObject(block, "content block", ownerId, errors, targets)) return;
        switch (block)
        {
            case SemanticChecklistBlock checklist:
                RequireCollection(checklist.Items, "checklist items", checklist.Id, errors, targets);
                break;
            case SemanticDrawingBlock drawing:
                RequireCollection(drawing.Strokes, "drawing strokes", drawing.Id, errors, targets);
                foreach (var stroke in drawing.Strokes ?? [])
                {
                    if (stroke is not null) RequireCollection(stroke.Points, "drawing points", stroke.Id, errors, targets);
                }
                break;
        }
    }

    private static bool RequireObject<T>(T? value, string field, string? ownerId, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets) where T : class
    {
        if (value is not null) return true;
        Add(errors, targets, "object_required", $"Required {field} must not be null.", ownerId, ["provide the required object"]);
        return false;
    }

    private static void RequireCollection<T>(IReadOnlyList<T>? values, string field, string? ownerId, ICollection<SemanticWorkspaceError> errors, IReadOnlyDictionary<string, int> targets) where T : class
    {
        if (values is null)
        {
            Add(errors, targets, "collection_required", $"Required {field} must be an array, not null.", ownerId, ["provide an array", "use an empty array when there are no entries"]);
        }
        else if (values.Any(item => item is null))
        {
            Add(errors, targets, "collection_entry_required", $"Required {field} must not contain null entries.", ownerId, ["remove null entries", "provide complete entries"]);
        }
    }

    private static void ValidateRelationshipEndpoint(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        SemanticRelationship relationship,
        string endpointId,
        string role,
        Dictionary<string, SemanticEntity> entityById)
    {
        if (!entityById.TryGetValue(endpointId, out var endpoint))
        {
            Add(errors, operationByTarget, $"relationship_{role}_missing", $"Relationship '{relationship.Id}' has unknown {role} '{endpointId}'.", endpointId, entityById.Keys, relationship.Id);
        }
        else if (!endpoint.DiagramId.Equals(relationship.DiagramId, StringComparison.Ordinal))
        {
            Add(errors, operationByTarget, $"relationship_{role}_wrong_diagram", $"Relationship '{relationship.Id}' and its {role} '{endpointId}' must belong to the same diagram.", endpointId, [relationship.DiagramId], relationship.Id);
        }
    }

    private static void ValidateTracking(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        SemanticTrackingFacet? tracking,
        string ownerId,
        IReadOnlyDictionary<string, SemanticKanbanBoard> boardById)
    {
        if (tracking is null)
        {
            return;
        }

        if (!boardById.TryGetValue(tracking.BoardId, out var board))
        {
            Add(errors, operationByTarget, "tracking_board_missing", $"Tracked object '{ownerId}' references unknown board '{tracking.BoardId}'.", tracking.BoardId, boardById.Keys, ownerId);
            return;
        }

        var columnIds = (board.Columns ?? []).Select(item => item.Id).ToArray();
        if (!columnIds.Contains(tracking.ColumnId, StringComparer.Ordinal))
        {
            Add(errors, operationByTarget, "tracking_column_missing", $"Tracked object '{ownerId}' references unknown column '{tracking.ColumnId}' on board '{board.Id}'.", tracking.ColumnId, columnIds, ownerId);
        }

        if (tracking.Progress.Mode != SemanticProgressMode.Manual)
        {
            Add(errors, operationByTarget, "progress_mode_not_implemented", $"Progress mode '{tracking.Progress.Mode}' is reserved but not implemented in schema version 1.", ownerId, [SemanticProgressMode.Manual.ToString()]);
        }

        if (tracking.Progress.Value is < 0 or > 100 || !IsFinite(tracking.Progress.Value))
        {
            Add(errors, operationByTarget, "progress_out_of_range", $"Progress for '{ownerId}' must be between 0 and 100.", ownerId, ["0", "100"]);
        }

        ValidateMetadata(errors, operationByTarget, tracking.Metadata, ownerId);
    }

    private static void ValidateView(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        SemanticDiagram diagram,
        IReadOnlyDictionary<string, SemanticEntity> entityById)
    {
        if (diagram.View is null)
        {
            Add(errors, operationByTarget, "diagram_view_required", $"Diagram '{diagram.Id}' requires presentation state.", diagram.Id, ["empty view"]);
            return;
        }

        if (diagram.View.Zoom is <= 0 || !IsFinite(diagram.View.Zoom)
            || !IsFinite(diagram.View.ViewportX)
            || !IsFinite(diagram.View.ViewportY))
        {
            Add(errors, operationByTarget, "diagram_viewport_invalid", $"Diagram '{diagram.Id}' has a non-finite viewport or non-positive zoom.", diagram.Id, ["finite coordinates", "positive zoom"]);
        }

        var presentations = diagram.View.Entities ?? [];
        ValidateUniqueIds(errors, operationByTarget, presentations.Select(item => item.EntityId), $"presentation in diagram '{diagram.Id}'");
        foreach (var presentation in presentations)
        {
            if (!entityById.TryGetValue(presentation.EntityId, out var entity)
                || !entity.DiagramId.Equals(diagram.Id, StringComparison.Ordinal))
            {
                Add(errors, operationByTarget, "presentation_entity_invalid", $"Diagram '{diagram.Id}' presentation references entity '{presentation.EntityId}' outside the diagram.", presentation.EntityId, entityById.Values.Where(item => item.DiagramId == diagram.Id).Select(item => item.Id), diagram.Id);
            }

            if (!IsFinite(presentation.X)
                || !IsFinite(presentation.Y)
                || presentation.Width is <= 0
                || presentation.Height is <= 0
                || !IsFinite(presentation.Width)
                || !IsFinite(presentation.Height))
            {
                Add(errors, operationByTarget, "presentation_geometry_invalid", $"Presentation for entity '{presentation.EntityId}' contains invalid geometry.", presentation.EntityId, ["finite coordinates", "positive dimensions"], diagram.Id);
            }
        }
    }

    private static void ValidateBlocks(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        SemanticEntity entity,
        HashSet<string> assetIds)
    {
        var blocks = entity.Blocks ?? [];
        foreach (var block in blocks)
        {
            RequireId(errors, operationByTarget, block.Id, "content block", block.Id, entity.Id);
            switch (block)
            {
                case SemanticTextBlock:
                    break;
                case SemanticChecklistBlock checklist:
                    ValidateUniqueOrders(errors, operationByTarget, (checklist.Items ?? []).Select(item => (item.Id, item.Order)), "checklist_order_duplicate");
                    foreach (var item in checklist.Items ?? [])
                    {
                        RequireId(errors, operationByTarget, item.Id, "checklist item", item.Id, checklist.Id, entity.Id);
                        RequireText(errors, operationByTarget, item.Text, "checklist_text_required", "Checklist item text is required.", item.Id, checklist.Id, entity.Id);
                        if (item.Order < 0)
                        {
                            Add(errors, operationByTarget, "checklist_order_invalid", $"Checklist item '{item.Id}' has a negative order.", item.Id, ["0 or greater"], checklist.Id, entity.Id);
                        }
                    }

                    break;
                case SemanticCodeBlock code:
                    RequireText(errors, operationByTarget, code.Language, "code_language_required", "Code block language is required.", code.Id, entity.Id);
                    break;
                case SemanticMathBlock math:
                    RequireText(errors, operationByTarget, math.Source, "math_source_required", "Math block LaTeX source is required.", math.Id, entity.Id);
                    break;
                case SemanticImageBlock image:
                    if (!assetIds.Contains(image.AssetId))
                    {
                        Add(errors, operationByTarget, "image_asset_missing", $"Image block '{image.Id}' references unknown asset '{image.AssetId}'.", image.AssetId, assetIds, image.Id, entity.Id);
                    }

                    RequireText(errors, operationByTarget, image.AltText, "image_alt_text_required", "Image block alt text is required.", image.Id, entity.Id);
                    break;
                case SemanticDrawingBlock drawing:
                    ValidateDrawing(errors, operationByTarget, drawing, entity.Id);
                    break;
                default:
                    Add(errors, operationByTarget, "content_kind_unsupported", $"Content block '{block.Id}' uses unsupported kind '{block.Kind}'.", block.Id, ["text", "checklist", "code", "math", "image", "drawing"], entity.Id);
                    break;
            }
        }
    }

    private static void ValidateDrawing(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        SemanticDrawingBlock drawing,
        string entityId)
    {
        var strokes = drawing.Strokes ?? [];
        foreach (var stroke in strokes)
        {
            RequireId(errors, operationByTarget, stroke.Id, "drawing stroke", stroke.Id, drawing.Id, entityId);
            if (!IsFinite(stroke.Width) || stroke.Width <= 0)
            {
                Add(errors, operationByTarget, "drawing_width_invalid", $"Drawing stroke '{stroke.Id}' requires a positive finite width.", stroke.Id, ["positive finite width"], drawing.Id, entityId);
            }

            RequireText(errors, operationByTarget, stroke.Color, "drawing_color_required", "Drawing stroke colour is required.", stroke.Id, drawing.Id, entityId);
            if ((stroke.Points ?? []).Any(point => !IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Pressure) || point.Pressure is < 0 or > 1))
            {
                Add(errors, operationByTarget, "drawing_point_invalid", $"Drawing stroke '{stroke.Id}' contains non-finite coordinates or pressure outside 0..1.", stroke.Id, ["finite coordinates", "pressure 0..1"], drawing.Id, entityId);
            }
        }
    }

    private static void ValidateDiagramCycles(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        IReadOnlyList<SemanticDiagram> diagrams,
        IReadOnlyList<SemanticEntity> entities,
        Dictionary<string, SemanticDiagram> diagramById)
    {
        var edges = diagrams.ToDictionary(item => item.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var diagram in diagrams)
        {
            if (diagram.ParentDiagramId is not null && edges.TryGetValue(diagram.ParentDiagramId, out var children))
            {
                children.Add(diagram.Id);
            }
        }

        foreach (var entity in entities.Where(item => item.ChildDiagramId is not null))
        {
            if (edges.TryGetValue(entity.DiagramId, out var children)
                && diagramById.ContainsKey(entity.ChildDiagramId!))
            {
                children.Add(entity.ChildDiagramId!);
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (!visiting.Add(id))
            {
                return true;
            }

            if (!visited.Add(id))
            {
                visiting.Remove(id);
                return false;
            }

            var cycle = edges.GetValueOrDefault(id)?.Any(Visit) == true;
            visiting.Remove(id);
            return cycle;
        }

        foreach (var diagramId in edges.Keys.Where(Visit))
        {
            Add(errors, operationByTarget, "child_diagram_cycle", $"Diagram '{diagramId}' participates in a child-diagram cycle.", diagramId, ["unlink one child diagram"]);
        }
    }

    private static void ValidatePointerCycles(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        IEnumerable<string> ids,
        Func<string, string?> next,
        string code,
        string message)
    {
        foreach (var start in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cursor = start;
            while (!string.IsNullOrWhiteSpace(cursor) && seen.Add(cursor))
            {
                cursor = next(cursor);
            }

            if (cursor is not null && seen.Contains(cursor))
            {
                Add(errors, operationByTarget, code, message, start, ["remove one parent link"]);
            }
        }
    }

    private static void ValidateUniqueIds(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        IEnumerable<string> ids,
        string kind)
    {
        foreach (var duplicate in ids.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Add(errors, operationByTarget, "duplicate_id", $"Stable {kind} ID '{duplicate.Key}' is duplicated.", duplicate.Key, ["choose a different ID", "omit the create ID to generate one"]);
        }
    }

    private static IEnumerable<string> EnumerateStableIds(SemanticWorkspaceModel workspace)
    {
        yield return workspace.Id;
        foreach (var diagram in workspace.Diagrams ?? []) yield return diagram.Id;
        foreach (var entity in workspace.Entities ?? [])
        {
            yield return entity.Id;
            foreach (var block in entity.Blocks ?? [])
            {
                yield return block.Id;
                if (block is SemanticChecklistBlock checklist)
                {
                    foreach (var item in checklist.Items ?? []) yield return item.Id;
                }
                else if (block is SemanticDrawingBlock drawing)
                {
                    foreach (var stroke in drawing.Strokes ?? []) yield return stroke.Id;
                }
            }
        }

        foreach (var relationship in workspace.Relationships ?? []) yield return relationship.Id;
        foreach (var board in workspace.Boards ?? [])
        {
            yield return board.Id;
            foreach (var column in board.Columns ?? []) yield return column.Id;
        }

        foreach (var asset in workspace.Assets ?? []) yield return asset.Id;
    }

    private static void ValidateUniqueOrders(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        IEnumerable<(string Id, int Order)> entries,
        string code)
    {
        foreach (var duplicate in entries.GroupBy(item => item.Order).Where(group => group.Count() > 1))
        {
            var ids = duplicate.Select(item => item.Id).ToArray();
            Add(errors, operationByTarget, code, $"Order {duplicate.Key} is assigned to multiple items.", ids[0], ["use distinct sequential order values"], ids);
        }
    }

    private static void ValidateMetadata(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        IReadOnlyDictionary<string, string>? metadata,
        string ownerId)
    {
        if (metadata is null)
        {
            Add(errors, operationByTarget, "metadata_required", $"Object '{ownerId}' requires a metadata collection.", ownerId, ["empty object"]);
            return;
        }

        if (metadata.Keys.Any(string.IsNullOrWhiteSpace))
        {
            Add(errors, operationByTarget, "metadata_key_invalid", $"Object '{ownerId}' contains an empty metadata key.", ownerId, ["non-empty key"]);
        }
    }

    private static void RequireId(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        string? value,
        string kind,
        string? targetId,
        params string[] owners)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, operationByTarget, "id_required", $"Every {kind} requires a non-empty stable ID.", targetId, ["provide an ID", "omit a create ID to generate one"], owners);
        }
    }

    private static void RequireText(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        string? value,
        string code,
        string message,
        string? targetId,
        params string[] owners)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, operationByTarget, code, message, targetId, ["provide a non-empty value"], owners);
        }
    }

    private static void RequireToken(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        string? value,
        string code,
        string targetId)
    {
        if (string.IsNullOrWhiteSpace(value) || !NormalizedToken().IsMatch(value))
        {
            Add(errors, operationByTarget, code, $"'{value}' is not a normalized semantic token.", targetId, ["lowercase-token", "lowercase-token-with-dashes"]);
        }
    }

    private static void Add(
        ICollection<SemanticWorkspaceError> errors,
        IReadOnlyDictionary<string, int> operationByTarget,
        string code,
        string message,
        string? targetId,
        IEnumerable<string> alternatives,
        params string[] ownerIds)
    {
        int? operationIndex = null;
        foreach (var candidate in new[] { targetId }.Concat(ownerIds.Cast<string?>()))
        {
            if (candidate is not null && operationByTarget.TryGetValue(candidate, out var index))
            {
                operationIndex = index;
                break;
            }
        }

        errors.Add(new SemanticWorkspaceError(
            code,
            message,
            operationIndex,
            targetId,
            alternatives.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Take(12).ToArray()));
    }

    private static bool IsFinite(double value) => double.IsFinite(value);

    private static bool IsFinite(double? value) => value is null || double.IsFinite(value.Value);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex NormalizedToken();
}

/// <summary>Reports that persisted or caller-supplied semantic state violates centralized invariants.</summary>
public sealed class SemanticWorkspaceValidationException : Exception
{
    /// <summary>Initializes a validation failure retaining every structured error.</summary>
    /// <param name="errors">The aggregate validation errors.</param>
    public SemanticWorkspaceValidationException(IReadOnlyList<SemanticWorkspaceError> errors)
        : base($"Semantic workspace validation failed with {errors.Count} error(s).")
    {
        Errors = errors;
    }

    /// <summary>Gets the complete structured validation errors.</summary>
    public IReadOnlyList<SemanticWorkspaceError> Errors { get; }
}
