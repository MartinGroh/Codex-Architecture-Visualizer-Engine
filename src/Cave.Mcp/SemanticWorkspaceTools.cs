using System.ComponentModel;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cave.Mcp;

/// <summary>
/// Exposes the canonical editable semantic workspace through bounded read models and atomic operations.
/// </summary>
[McpServerToolType]
public sealed class SemanticWorkspaceTools(SemanticWorkspaceService semanticWorkspaces)
{
    private static readonly JsonSerializerOptions JsonOptions = SemanticWorkspaceJson.CreateOptions();

    /// <summary>Reads the complete canonical semantic workspace.</summary>
    [McpServerTool(
        Name = "cave_get_semantic_workspace",
        Title = "Get CAVE semantic workspace",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Read the complete schema-versioned CAVE semantic workspace. This user-authored state may bind to CodeGraph node IDs but never mutates the CodeGraph index.")]
    public async Task<CallToolResult> GetWorkspaceAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var workspace = await semanticWorkspaces.GetAsync(root, cancellationToken).ConfigureAwait(false);
        return CreateResult(workspace, $"Loaded semantic workspace '{workspace.Name}' at revision {workspace.Revision}.");
    }

    /// <summary>Reads a compact deterministic summary without presentation state.</summary>
    [McpServerTool(
        Name = "cave_get_semantic_summary",
        Title = "Get CAVE semantic summary",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Read counts and deterministic per-diagram summaries for the CAVE semantic workspace without layout state.")]
    public async Task<CallToolResult> GetSummaryAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var workspace = await semanticWorkspaces.GetAsync(root, cancellationToken).ConfigureAwait(false);
        return CreateResult(
            SemanticWorkspaceReadModelService.GetWorkspaceSummary(workspace),
            $"Loaded semantic summary for '{workspace.Name}'.");
    }

    /// <summary>Reads one diagram and its canonical owned objects.</summary>
    [McpServerTool(
        Name = "cave_get_semantic_diagram",
        Title = "Get CAVE semantic diagram",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Read one semantic diagram with its entities, typed relationships, child diagrams, referenced assets, and tracking boards.")]
    public async Task<CallToolResult> GetDiagramAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Stable semantic diagram identifier.")] string diagramId,
        CancellationToken cancellationToken)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var workspace = await semanticWorkspaces.GetAsync(root, cancellationToken).ConfigureAwait(false);
        var diagram = SemanticWorkspaceReadModelService.FindDiagram(workspace, diagramId);
        return diagram is null
            ? CreateError("diagram_not_found", $"Semantic diagram '{diagramId}' does not exist.", diagramId)
            : CreateResult(diagram, $"Loaded semantic diagram '{diagram.Diagram.Title}'.");
    }

    /// <summary>Reads bounded semantic context around one entity.</summary>
    [McpServerTool(
        Name = "cave_get_semantic_entity_context",
        Title = "Get CAVE semantic entity context",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Read bounded typed-relation, hierarchy, and child-diagram context around one semantic entity.")]
    public async Task<CallToolResult> GetEntityContextAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Stable semantic entity identifier.")] string entityId,
        [Description("Traversal depth from 0 through 8.")] int depth = 1,
        [Description("Maximum returned entities from 1 through 500.")] int maximumEntities = SemanticWorkspaceReadModelService.DefaultMaximumEntities,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var workspace = await semanticWorkspaces.GetAsync(root, cancellationToken).ConfigureAwait(false);
        try
        {
            var context = SemanticWorkspaceReadModelService.FindEntityContext(
                workspace,
                entityId,
                depth,
                maximumEntities);
            return context is null
                ? CreateError("entity_not_found", $"Semantic entity '{entityId}' does not exist.", entityId)
                : CreateResult(
                    context,
                    $"Loaded {context.Entities.Count} semantic entities around '{entityId}' at depth {depth}.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return CreateError("invalid_context_bounds", exception.Message, entityId);
        }
    }

    /// <summary>Validates and atomically applies typed semantic operations.</summary>
    [McpServerTool(
        Name = "cave_apply_semantic_operations",
        Title = "Apply CAVE semantic operations",
        ReadOnly = false)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Validate and atomically commit a typed, actor-attributed semantic-workspace batch. Rejected batches return structured errors and never partially commit.")]
    public async Task<CallToolResult> ApplyOperationsAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Actor-attributed batch. Each operation uses the 'operation' discriminator.")] SemanticWorkspaceBatch batch,
        CancellationToken cancellationToken)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var result = await semanticWorkspaces.ApplyOperationsAsync(root, batch, cancellationToken)
            .ConfigureAwait(false);
        return CreateResult(
            result,
            result.Succeeded
                ? $"Committed semantic transaction '{result.Transaction!.Id}' at revision {result.Workspace.Revision}."
                : $"Rejected semantic batch with {result.Errors.Count} validation error(s); no state was changed.");
    }

    /// <summary>Undoes the latest semantic batch in this service session.</summary>
    [McpServerTool(
        Name = "cave_undo_semantic_workspace",
        Title = "Undo CAVE semantic workspace batch",
        ReadOnly = false)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Atomically restore the semantic workspace state before the latest batch committed in this service session.")]
    public async Task<CallToolResult> UndoAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Actor requesting the undo; defaults to Codex.")] SemanticActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var result = await semanticWorkspaces.UndoAsync(
            root,
            actor ?? DefaultCodexActor,
            cancellationToken).ConfigureAwait(false);
        return CreateResult(
            result,
            result.Succeeded
                ? $"Undid the latest semantic batch; workspace revision is {result.Workspace.Revision}."
                : "No semantic batch was undone.");
    }

    /// <summary>Redoes the latest semantic batch undone in this service session.</summary>
    [McpServerTool(
        Name = "cave_redo_semantic_workspace",
        Title = "Redo CAVE semantic workspace batch",
        ReadOnly = false)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Atomically reapply the latest semantic batch undone in this service session.")]
    public async Task<CallToolResult> RedoAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Actor requesting the redo; defaults to Codex.")] SemanticActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var result = await semanticWorkspaces.RedoAsync(
            root,
            actor ?? DefaultCodexActor,
            cancellationToken).ConfigureAwait(false);
        return CreateResult(
            result,
            result.Succeeded
                ? $"Redid the semantic batch; workspace revision is {result.Workspace.Revision}."
                : "No semantic batch was redone.");
    }

    /// <summary>Exports canonical semantic state as JSON or readable Markdown.</summary>
    [McpServerTool(
        Name = "cave_export_semantic_workspace",
        Title = "Export CAVE semantic workspace",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Export the semantic workspace as versioned canonical JSON or human-readable Markdown. Presentation state is excluded unless explicitly requested.")]
    public async Task<CallToolResult> ExportAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Export format: 'json' or 'markdown'.")] string format = "json",
        [Description("Include diagram presentation and layout state in JSON export.")] bool includePresentation = false,
        CancellationToken cancellationToken = default)
    {
        var root = ResolveWorkspaceRoot(workspacePath);
        var workspace = await semanticWorkspaces.GetAsync(root, cancellationToken).ConfigureAwait(false);
        SemanticWorkspaceExportResult result;
        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            result = new SemanticWorkspaceExportResult(
                SemanticWorkspaceExportService.ExportJson(
                    workspace,
                    new SemanticWorkspaceExportOptions(includePresentation, WriteIndented: true)));
        }
        else if (format.Equals("markdown", StringComparison.OrdinalIgnoreCase)
                 || format.Equals("md", StringComparison.OrdinalIgnoreCase))
        {
            result = new SemanticWorkspaceExportResult(
                SemanticWorkspaceExportService.ExportHumanReadable(workspace));
        }
        else
        {
            return CreateError(
                "unsupported_export_format",
                $"Semantic export format '{format}' is unsupported. Choose 'json' or 'markdown'.",
                null);
        }

        return CreateResult(result, $"Exported semantic workspace '{workspace.Name}' as {format}.");
    }

    private static SemanticActor DefaultCodexActor { get; } = new(
        SemanticActorType.Codex,
        "codex",
        "Codex");

    private static string ResolveWorkspaceRoot(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        if (!Path.IsPathFullyQualified(workspacePath))
        {
            throw new ArgumentException("CAVE requires an absolute semantic-workspace root.", nameof(workspacePath));
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace root '{root}' does not exist.");
        }

        return root;
    }

    private static CallToolResult CreateResult<T>(T result, string message) =>
        new()
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions),
        };

    private static CallToolResult CreateError(string code, string message, string? targetId) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(
                new SemanticWorkspaceToolError(code, message, targetId),
                JsonOptions),
        };
}

/// <summary>Contains one semantic-workspace export produced by an MCP adapter.</summary>
/// <param name="Text">The complete deterministic export.</param>
public sealed record SemanticWorkspaceExportResult(string Text);

/// <summary>Provides a structured semantic-workspace MCP diagnostic.</summary>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Message">Actionable diagnostic.</param>
/// <param name="TargetId">Related semantic identifier, when any.</param>
public sealed record SemanticWorkspaceToolError(
    string Code,
    string Message,
    string? TargetId);
