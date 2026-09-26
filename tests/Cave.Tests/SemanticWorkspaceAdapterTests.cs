using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.SemanticWorkspace;
using Cave.Infrastructure.Workspaces;
using Cave.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cave.Tests;

/// <summary>
/// Verifies that HTTP and MCP adapters compose the same canonical semantic-workspace services.
/// </summary>
/// <param name="factory">The in-memory CAVE host factory.</param>
public sealed class SemanticWorkspaceAdapterTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// Verifies the HTTP adapter's canonical operation discriminator, bounded reads, export, undo, and redo.
    /// </summary>
    [Fact]
    public async Task HttpAdapterProjectsOneCanonicalSemanticWorkspace()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        var catalogRoot = Path.Combine(Path.GetTempPath(), "cave-semantic-adapter-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var testFactory = factory.WithWebHostBuilder(builder => builder
                .UseSetting("Cave:SemanticProvider", "Sample")
                .UseSetting("Cave:WorkspaceRoot", workspaceRoot)
                .UseSetting("Cave:WorkspaceCatalogRoot", catalogRoot));
            using var client = testFactory.CreateClient();
            var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(workspaceRoot);

            var initial = await client.GetFromJsonAsync<SemanticWorkspace>(
                $"/api/semantic-workspace?workspace={workspaceId}",
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(initial);
            Assert.Empty(initial.Diagrams);

            using var applyResponse = await client.PostAsync(
                $"/api/semantic-workspace/operations?workspace={workspaceId}",
                new StringContent(CreateBatchJson(), Encoding.UTF8, "application/json"),
                CancellationToken.None);
            applyResponse.EnsureSuccessStatusCode();
            var applied = await applyResponse.Content.ReadFromJsonAsync<SemanticWorkspaceOperationResult>(
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(applied);
            Assert.True(applied.Succeeded);
            Assert.Equal(1, applied.Workspace.Revision);

            var summary = await client.GetFromJsonAsync<SemanticWorkspaceSummary>(
                $"/api/semantic-workspace/summary?workspace={workspaceId}",
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(summary);
            Assert.Equal(1, summary.DiagramCount);
            Assert.Equal(1, summary.EntityCount);

            var diagram = await client.GetFromJsonAsync<SemanticDiagramReadModel>(
                $"/api/semantic-workspace/diagrams/diagram-system?workspace={workspaceId}",
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(diagram);
            Assert.Equal("System design", diagram.Diagram.Title);
            Assert.Equal("entity-api", Assert.Single(diagram.Entities).Id);

            var context = await client.GetFromJsonAsync<SemanticEntityContext>(
                $"/api/semantic-workspace/entities/entity-api/context?workspace={workspaceId}&depth=0&maximumEntities=10",
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(context);
            Assert.Equal(0, context.RequestedDepth);
            Assert.Equal("entity-api", Assert.Single(context.Entities).Entity.Id);

            using var jsonExportResponse = await client.GetAsync(
                $"/api/semantic-workspace/export?workspace={workspaceId}&format=json&includePresentation=true",
                CancellationToken.None);
            jsonExportResponse.EnsureSuccessStatusCode();
            using var jsonExportEnvelope = JsonDocument.Parse(
                await jsonExportResponse.Content.ReadAsStreamAsync(CancellationToken.None));
            var exportedJson = jsonExportEnvelope.RootElement.GetProperty("text").GetString()!;
            using var exportDocument = JsonDocument.Parse(exportedJson);
            Assert.Equal(
                SemanticWorkspaceExportService.Format,
                exportDocument.RootElement.GetProperty("format").GetString());
            Assert.NotEqual(
                JsonValueKind.Null,
                exportDocument.RootElement
                    .GetProperty("workspace")
                    .GetProperty("diagrams")[0]
                    .GetProperty("view")
                    .ValueKind);

            using var markdownExportResponse = await client.GetAsync(
                $"/api/semantic-workspace/export?workspace={workspaceId}&format=markdown",
                CancellationToken.None);
            markdownExportResponse.EnsureSuccessStatusCode();
            using var markdownEnvelope = JsonDocument.Parse(
                await markdownExportResponse.Content.ReadAsStreamAsync(CancellationToken.None));
            Assert.Contains("## System design", markdownEnvelope.RootElement.GetProperty("text").GetString());

            using var undoResponse = await client.PostAsync(
                $"/api/semantic-workspace/undo?workspace={workspaceId}",
                content: null,
                CancellationToken.None);
            undoResponse.EnsureSuccessStatusCode();
            var undone = await undoResponse.Content.ReadFromJsonAsync<SemanticWorkspaceOperationResult>(
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(undone);
            Assert.True(undone.Succeeded);
            Assert.Empty(undone.Workspace.Diagrams);

            using var redoResponse = await client.PostAsync(
                $"/api/semantic-workspace/redo?workspace={workspaceId}",
                content: null,
                CancellationToken.None);
            redoResponse.EnsureSuccessStatusCode();
            var redone = await redoResponse.Content.ReadFromJsonAsync<SemanticWorkspaceOperationResult>(
                SemanticWorkspaceJson.CreateOptions(),
                CancellationToken.None);
            Assert.NotNull(redone);
            Assert.True(redone.Succeeded);
            Assert.Equal("diagram-system", Assert.Single(redone.Workspace.Diagrams).Id);
        }
        finally
        {
            if (Directory.Exists(catalogRoot))
            {
                Directory.Delete(catalogRoot, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies all MCP operations use the same aggregate, read models, exports, and undo history.
    /// </summary>
    [Fact]
    public async Task McpToolsExposeCanonicalReadsWritesAndExports()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        try
        {
            var service = new SemanticWorkspaceService(new FileSemanticWorkspaceStore(), TimeProvider.System);
            var tools = new SemanticWorkspaceTools(service);
            var batch = CreateBatch();

            var initial = await tools.GetWorkspaceAsync(workspaceRoot, CancellationToken.None);
            Assert.Equal(0, initial.StructuredContent!.Value.GetProperty("revision").GetInt64());

            var apply = await tools.ApplyOperationsAsync(workspaceRoot, batch, CancellationToken.None);
            Assert.True(apply.StructuredContent!.Value.GetProperty("succeeded").GetBoolean());

            var summary = await tools.GetSummaryAsync(workspaceRoot, CancellationToken.None);
            Assert.Equal(1, summary.StructuredContent!.Value.GetProperty("entityCount").GetInt32());

            var diagram = await tools.GetDiagramAsync(workspaceRoot, "diagram-system", CancellationToken.None);
            Assert.Equal(
                "entity-api",
                diagram.StructuredContent!.Value.GetProperty("entities")[0].GetProperty("id").GetString());

            var context = await tools.GetEntityContextAsync(
                workspaceRoot,
                "entity-api",
                depth: 0,
                maximumEntities: 10,
                CancellationToken.None);
            Assert.Equal(1, context.StructuredContent!.Value.GetProperty("entities").GetArrayLength());

            var export = await tools.ExportAsync(
                workspaceRoot,
                "json",
                includePresentation: false,
                CancellationToken.None);
            var exportText = export.StructuredContent!.Value.GetProperty("text").GetString()!;
            Assert.Contains("\"format\": \"cave-semantic-workspace\"", exportText);

            var undo = await tools.UndoAsync(workspaceRoot, cancellationToken: CancellationToken.None);
            Assert.True(undo.StructuredContent!.Value.GetProperty("succeeded").GetBoolean());
            var redo = await tools.RedoAsync(workspaceRoot, cancellationToken: CancellationToken.None);
            Assert.True(redo.StructuredContent!.Value.GetProperty("succeeded").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static SemanticWorkspaceBatch CreateBatch() => new(
        new SemanticActor(SemanticActorType.Human, "test-user", "Test user"),
        "Create a system diagram and its API entity",
        [
            new CreateDiagramOperation(new SemanticDiagram(
                "diagram-system",
                "System design",
                "Describe the system boundary.",
                ParentDiagramId: null,
                Tracking: null,
                new SemanticDiagramView(1, 0, 0, []),
                new Dictionary<string, string>())),
            new CreateEntityOperation(new SemanticEntity(
                "entity-api",
                "diagram-system",
                "service",
                "Public API",
                "HTTP entry point",
                ParentEntityId: null,
                ChildDiagramId: null,
                ArchitectureNodeId: "project:Cave.Host",
                Blocks: [],
                Tracking: null,
                Tags: ["api"],
                Metadata: new Dictionary<string, string>())),
        ]);

    private static string CreateBatchJson() => JsonSerializer.Serialize(
        CreateBatch(),
        SemanticWorkspaceJson.CreateOptions());

    private static string CreateWorkspaceRoot()
    {
        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "cave-semantic-adapter-tests",
            Guid.NewGuid().ToString("N"),
            "Workspace");
        var codeGraphRoot = Path.Combine(workspaceRoot, ".codegraph");
        Directory.CreateDirectory(codeGraphRoot);
        File.WriteAllBytes(Path.Combine(codeGraphRoot, "codegraph.db"), []);
        return workspaceRoot;
    }
}
