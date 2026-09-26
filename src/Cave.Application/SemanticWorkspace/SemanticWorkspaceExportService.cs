using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Cave.Domain;

namespace Cave.Application;

/// <summary>Controls the canonical semantic-workspace export projection.</summary>
/// <param name="IncludePresentationState">Whether diagram-local view and layout state is included.</param>
/// <param name="WriteIndented">Whether JSON output is formatted for human inspection.</param>
public sealed record SemanticWorkspaceExportOptions(
    bool IncludePresentationState = false,
    bool WriteIndented = true);

/// <summary>Defines the versioned, import-ready semantic-workspace export envelope.</summary>
/// <param name="Format">The stable export format identifier.</param>
/// <param name="FormatVersion">The export-envelope version.</param>
/// <param name="Workspace">The canonical semantic payload.</param>
public sealed record SemanticWorkspaceExportDocument(
    string Format,
    int FormatVersion,
    SemanticWorkspaceExportPayload Workspace);

/// <summary>Contains schema-versioned semantic workspace state without adapter-specific fields.</summary>
/// <param name="SchemaVersion">The semantic-workspace schema version.</param>
/// <param name="Id">The stable workspace identifier.</param>
/// <param name="Name">The workspace name.</param>
/// <param name="Revision">The committed semantic revision.</param>
/// <param name="UpdatedAtUtc">The last committed update time.</param>
/// <param name="Diagrams">The semantic diagrams, with optional presentation state.</param>
/// <param name="Entities">The semantic entities and rich content.</param>
/// <param name="Relationships">The typed semantic relationships.</param>
/// <param name="Boards">The configurable tracking boards.</param>
/// <param name="Assets">The referenced assets and provenance metadata.</param>
public sealed record SemanticWorkspaceExportPayload(
    int SchemaVersion,
    string Id,
    string Name,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SemanticDiagramExportModel> Diagrams,
    IReadOnlyList<SemanticEntity> Entities,
    IReadOnlyList<SemanticRelationship> Relationships,
    IReadOnlyList<SemanticKanbanBoard> Boards,
    IReadOnlyList<SemanticAssetReference> Assets);

/// <summary>Represents a semantic diagram in an export document.</summary>
/// <param name="Id">The stable diagram identifier.</param>
/// <param name="Title">The diagram title.</param>
/// <param name="Purpose">The optional semantic purpose.</param>
/// <param name="ParentDiagramId">The optional parent diagram identifier.</param>
/// <param name="Tracking">The optional tracking facet.</param>
/// <param name="Metadata">The diagram metadata.</param>
/// <param name="View">Presentation state when explicitly requested; otherwise omitted.</param>
public sealed record SemanticDiagramExportModel(
    string Id,
    string Title,
    string? Purpose,
    string? ParentDiagramId,
    SemanticTrackingFacet? Tracking,
    IReadOnlyDictionary<string, string> Metadata,
    SemanticDiagramView? View);

/// <summary>
/// Projects the canonical semantic aggregate into deterministic JSON or a human-readable Markdown document.
/// </summary>
public static class SemanticWorkspaceExportService
{
    /// <summary>Gets the stable semantic export format identifier.</summary>
    public const string Format = "cave-semantic-workspace";

    /// <summary>Gets the current export-envelope version.</summary>
    public const int FormatVersion = 1;

    /// <summary>Creates the versioned semantic export document.</summary>
    /// <param name="workspace">The canonical semantic workspace.</param>
    /// <param name="options">Export options; presentation state is excluded by default.</param>
    /// <returns>A deterministic, adapter-independent export model.</returns>
    public static SemanticWorkspaceExportDocument CreateDocument(
        Cave.Domain.SemanticWorkspace workspace,
        SemanticWorkspaceExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        options ??= new SemanticWorkspaceExportOptions();

        var diagrams = workspace.Diagrams
            .OrderBy(diagram => diagram.Id, StringComparer.Ordinal)
            .Select(diagram => new SemanticDiagramExportModel(
                diagram.Id,
                diagram.Title,
                diagram.Purpose,
                diagram.ParentDiagramId,
                NormalizeTracking(diagram.Tracking),
                SortMetadata(diagram.Metadata),
                options.IncludePresentationState ? NormalizeView(diagram.View) : null))
            .ToArray();

        var entities = workspace.Entities
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .Select(NormalizeEntity)
            .ToArray();

        var relationships = workspace.Relationships
            .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
            .Select(relationship => relationship with { Metadata = SortMetadata(relationship.Metadata) })
            .ToArray();

        var boards = workspace.Boards
            .OrderBy(board => board.Id, StringComparer.Ordinal)
            .Select(board => board with
            {
                Columns = board.Columns
                    .OrderBy(column => column.Order)
                    .ThenBy(column => column.Id, StringComparer.Ordinal)
                    .ToArray(),
                Metadata = SortMetadata(board.Metadata),
            })
            .ToArray();

        var assets = workspace.Assets
            .OrderBy(asset => asset.Id, StringComparer.Ordinal)
            .Select(asset => asset with { Metadata = SortMetadata(asset.Metadata) })
            .ToArray();

        return new SemanticWorkspaceExportDocument(
            Format,
            FormatVersion,
            new SemanticWorkspaceExportPayload(
                workspace.SchemaVersion,
                workspace.Id,
                workspace.Name,
                workspace.Revision,
                workspace.UpdatedAtUtc,
                diagrams,
                entities,
                relationships,
                boards,
                assets));
    }

    /// <summary>Serializes the canonical semantic export document as camel-case JSON.</summary>
    /// <param name="workspace">The canonical semantic workspace.</param>
    /// <param name="options">Export options; presentation state is excluded by default.</param>
    /// <returns>Versioned semantic JSON suitable for a future importer.</returns>
    public static string ExportJson(
        Cave.Domain.SemanticWorkspace workspace,
        SemanticWorkspaceExportOptions? options = null)
    {
        options ??= new SemanticWorkspaceExportOptions();
        var serializerOptions = CreateSerializerOptions(options.WriteIndented);
        return JsonSerializer.Serialize(CreateDocument(workspace, options), serializerOptions);
    }

    /// <summary>Projects the semantic workspace into a readable Markdown document.</summary>
    /// <param name="workspace">The canonical semantic workspace.</param>
    /// <returns>A complete human-readable semantic projection without diagram layout.</returns>
    public static string ExportHumanReadable(Cave.Domain.SemanticWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var builder = new StringBuilder();
        var assets = workspace.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);

        builder.Append("# ").AppendLine(workspace.Name);
        builder.AppendLine();
        builder.Append("- Workspace ID: `").Append(workspace.Id).AppendLine("`");
        builder.Append("- Semantic schema: ").AppendLine(workspace.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        builder.Append("- Revision: ").AppendLine(workspace.Revision.ToString(CultureInfo.InvariantCulture));
        builder.Append("- Updated: ").AppendLine(workspace.UpdatedAtUtc.ToString("O"));

        foreach (var diagram in workspace.Diagrams.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.AppendLine().Append("## ").AppendLine(diagram.Title);
            builder.AppendLine();
            builder.Append("Diagram ID: `").Append(diagram.Id).AppendLine("`");
            if (!string.IsNullOrWhiteSpace(diagram.Purpose))
            {
                builder.AppendLine().AppendLine(diagram.Purpose);
            }

            AppendParentAndTracking(builder, diagram.ParentDiagramId, diagram.Tracking);
            AppendMetadata(builder, diagram.Metadata);

            var diagramEntities = workspace.Entities
                .Where(entity => string.Equals(entity.DiagramId, diagram.Id, StringComparison.Ordinal))
                .OrderBy(entity => entity.Id, StringComparer.Ordinal)
                .ToArray();

            if (diagramEntities.Length > 0)
            {
                builder.AppendLine().AppendLine("### Entities");
            }

            foreach (var entity in diagramEntities)
            {
                builder.AppendLine().Append("#### ").AppendLine(entity.Title);
                builder.AppendLine();
                builder.Append("- Entity ID: `").Append(entity.Id).AppendLine("`");
                builder.Append("- Type: `").Append(entity.Type).AppendLine("`");
                AppendEntityReferences(builder, entity);
                AppendTracking(builder, entity.Tracking);
                if (entity.Tags.Count > 0)
                {
                    builder.Append("- Tags: ").AppendLine(string.Join(", ", entity.Tags.Order(StringComparer.Ordinal).Select(tag => $"`{tag}`")));
                }

                if (!string.IsNullOrWhiteSpace(entity.Description))
                {
                    builder.AppendLine().AppendLine(entity.Description);
                }

                foreach (var block in entity.Blocks)
                {
                    AppendBlock(builder, block, assets);
                }

                AppendMetadata(builder, entity.Metadata);
            }

            var relationships = workspace.Relationships
                .Where(relationship => string.Equals(relationship.DiagramId, diagram.Id, StringComparison.Ordinal))
                .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
                .ToArray();

            if (relationships.Length > 0)
            {
                builder.AppendLine().AppendLine("### Relationships");
                builder.AppendLine();
                foreach (var relationship in relationships)
                {
                    builder.Append("- `")
                        .Append(relationship.SourceEntityId)
                        .Append("` --")
                        .Append(relationship.Type)
                        .Append("--> `")
                        .Append(relationship.TargetEntityId)
                        .Append('`');
                    if (!string.IsNullOrWhiteSpace(relationship.Label))
                    {
                        builder.Append(": ").Append(relationship.Label);
                    }

                    builder.AppendLine();
                }
            }
        }

        if (workspace.Boards.Count > 0)
        {
            builder.AppendLine().AppendLine("## Tracking boards");
            foreach (var board in workspace.Boards.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                builder.AppendLine().Append("### ").AppendLine(board.Title);
                builder.AppendLine();
                foreach (var column in board.Columns.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    builder.Append("- ").Append(column.Order).Append(". ").Append(column.Title)
                        .Append(" (`").Append(column.Id).AppendLine("`)");
                }
            }
        }

        if (workspace.Assets.Count > 0)
        {
            builder.AppendLine().AppendLine("## Assets");
            builder.AppendLine();
            foreach (var asset in workspace.Assets.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                builder.Append("- `").Append(asset.Id).Append("`: ")
                    .Append(asset.RelativePath).Append(" (").Append(asset.MediaType).AppendLine(")");
                AppendMetadata(builder, asset.Metadata);
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static JsonSerializerOptions CreateSerializerOptions(bool writeIndented)
    {
        var options = SemanticWorkspaceJson.CreateOptions(writeIndented);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }

    private static SemanticEntity NormalizeEntity(SemanticEntity entity) => entity with
    {
        Blocks = entity.Blocks.Select(NormalizeBlock).ToArray(),
        Tracking = NormalizeTracking(entity.Tracking),
        Tags = entity.Tags.Order(StringComparer.Ordinal).ToArray(),
        Metadata = SortMetadata(entity.Metadata),
    };

    private static SemanticContentBlock NormalizeBlock(SemanticContentBlock block) => block switch
    {
        SemanticChecklistBlock checklist => checklist with
        {
            Items = checklist.Items
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray(),
        },
        SemanticDrawingBlock drawing => drawing with
        {
            Strokes = drawing.Strokes
                .Select(stroke => stroke with { Points = stroke.Points.ToArray() })
                .ToArray(),
        },
        _ => block,
    };

    private static SemanticTrackingFacet? NormalizeTracking(SemanticTrackingFacet? tracking) => tracking is null
        ? null
        : tracking with { Metadata = SortMetadata(tracking.Metadata) };

    private static SemanticDiagramView NormalizeView(SemanticDiagramView view) => view with
    {
        Entities = view.Entities.OrderBy(item => item.EntityId, StringComparer.Ordinal).ToArray(),
    };

    private static SortedDictionary<string, string> SortMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            sorted.Add(pair.Key, pair.Value);
        }

        return sorted;
    }

    private static void AppendParentAndTracking(
        StringBuilder builder,
        string? parentDiagramId,
        SemanticTrackingFacet? tracking)
    {
        if (!string.IsNullOrWhiteSpace(parentDiagramId))
        {
            builder.Append("- Parent diagram: `").Append(parentDiagramId).AppendLine("`");
        }

        AppendTracking(builder, tracking);
    }

    private static void AppendEntityReferences(StringBuilder builder, SemanticEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ParentEntityId))
        {
            builder.Append("- Parent entity: `").Append(entity.ParentEntityId).AppendLine("`");
        }

        if (!string.IsNullOrWhiteSpace(entity.ChildDiagramId))
        {
            builder.Append("- Child diagram: `").Append(entity.ChildDiagramId).AppendLine("`");
        }

        if (!string.IsNullOrWhiteSpace(entity.ArchitectureNodeId))
        {
            builder.Append("- Architecture node: `").Append(entity.ArchitectureNodeId).AppendLine("`");
        }
    }

    private static void AppendTracking(StringBuilder builder, SemanticTrackingFacet? tracking)
    {
        if (tracking is null)
        {
            return;
        }

        builder.Append("- Tracking: board `").Append(tracking.BoardId)
            .Append("`, column `").Append(tracking.ColumnId).Append('`');
        if (tracking.Progress.Value is not null)
        {
            builder.Append(", progress ").Append(tracking.Progress.Value.Value.ToString("0.##", CultureInfo.InvariantCulture))
                .Append("% (").Append(tracking.Progress.Mode).Append(')');
        }

        builder.AppendLine();
    }

    private static void AppendBlock(
        StringBuilder builder,
        SemanticContentBlock block,
        Dictionary<string, SemanticAssetReference> assets)
    {
        builder.AppendLine();
        switch (block)
        {
            case SemanticTextBlock text:
                builder.AppendLine(text.Text);
                break;
            case SemanticChecklistBlock checklist:
                foreach (var item in checklist.Items.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
                {
                    builder.Append("- [").Append(item.Completed ? 'x' : ' ').Append("] ").AppendLine(item.Text);
                }

                break;
            case SemanticCodeBlock code:
                if (!string.IsNullOrWhiteSpace(code.Filename))
                {
                    builder.Append("`File: ").Append(code.Filename).AppendLine("`");
                }

                builder.Append("```").AppendLine(code.Language).AppendLine(code.Source).AppendLine("```");
                break;
            case SemanticMathBlock math:
                builder.AppendLine("```math").AppendLine(math.Source).AppendLine("```");
                break;
            case SemanticImageBlock image:
                var path = assets.TryGetValue(image.AssetId, out var asset)
                    ? asset.RelativePath
                    : $"asset:{image.AssetId}";
                builder.Append("![");
                builder.Append(image.AltText).Append("](").Append(path).AppendLine(")");
                if (!string.IsNullOrWhiteSpace(image.Caption))
                {
                    builder.Append('_').Append(image.Caption).AppendLine("_");
                }

                break;
            case SemanticDrawingBlock drawing:
                builder.Append("_Editable vector drawing with ").Append(drawing.Strokes.Count).AppendLine(" stroke(s)._");
                break;
            default:
                builder.Append("_Unsupported content block `").Append(block.Kind).AppendLine("`._");
                break;
        }
    }

    private static void AppendMetadata(StringBuilder builder, IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        builder.AppendLine().AppendLine("Metadata:");
        foreach (var pair in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.Append("- `").Append(pair.Key).Append("`: ").AppendLine(pair.Value);
        }
    }

}
