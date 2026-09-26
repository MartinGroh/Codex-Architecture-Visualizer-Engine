using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>Verifies semantic workspace summaries, diagram reads, and bounded context.</summary>
public sealed class SemanticWorkspaceReadModelTests
{
    /// <summary>Workspace and diagram summaries count the canonical objects without creating Kanban cards.</summary>
    [Fact]
    public void GetWorkspaceSummaryCountsCanonicalSemanticState()
    {
        var summary = SemanticWorkspaceReadModelService.GetWorkspaceSummary(
            SemanticWorkspaceReadModelTestData.CreateWorkspace());

        Assert.Equal("workspace:engineering", summary.Id);
        Assert.Equal(3, summary.DiagramCount);
        Assert.Equal(4, summary.EntityCount);
        Assert.Equal(1, summary.RelationshipCount);
        Assert.Equal(1, summary.BoardCount);
        Assert.Equal(1, summary.AssetCount);
        Assert.Equal(1, summary.TrackedDiagramCount);
        Assert.Equal(1, summary.TrackedEntityCount);
        var main = Assert.Single(summary.Diagrams, diagram => diagram.Id == "diagram:main");
        Assert.Equal(2, main.EntityCount);
        Assert.Equal(1, main.RelationshipCount);
        Assert.Equal(1, main.ChildDiagramCount);
        Assert.Equal(6, main.ContentBlockCount);
        var directSummary = SemanticWorkspaceReadModelService.FindDiagramSummary(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "diagram:main");
        Assert.NotNull(directSummary);
        Assert.Equal(main.Id, directSummary.Id);
        Assert.Equal(main.EntityCount, directSummary.EntityCount);
        Assert.Equal(main.RelationshipCount, directSummary.RelationshipCount);
        Assert.Null(SemanticWorkspaceReadModelService.FindDiagramSummary(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "diagram:missing"));
    }

    /// <summary>A full diagram read contains only its canonical entities, relations, assets, boards, and child links.</summary>
    [Fact]
    public void FindDiagramReturnsCompleteScopedReadModel()
    {
        var workspace = SemanticWorkspaceReadModelTestData.CreateWorkspace();

        var readModel = SemanticWorkspaceReadModelService.FindDiagram(workspace, "diagram:main");

        Assert.NotNull(readModel);
        Assert.Equal(["entity:related", "entity:root"], readModel.Entities.Select(entity => entity.Id).ToArray());
        Assert.Equal("relationship:depends", Assert.Single(readModel.Relationships).Id);
        Assert.Equal("diagram:child", Assert.Single(readModel.ChildDiagrams).Id);
        Assert.Equal("asset:diagram", Assert.Single(readModel.ReferencedAssets).Id);
        Assert.Equal("board:delivery", Assert.Single(readModel.TrackingBoards).Id);
        Assert.Equal(1.25, readModel.Diagram.View.Zoom);
        Assert.Null(SemanticWorkspaceReadModelService.FindDiagram(workspace, "diagram:missing"));
    }

    /// <summary>Context follows typed relations, entity hierarchy, and child diagrams up to the requested depth.</summary>
    [Fact]
    public void FindEntityContextTraversesSemanticConnectionsWithinDepth()
    {
        var context = SemanticWorkspaceReadModelService.FindEntityContext(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "entity:root",
            depth: 1,
            maximumEntities: 10);

        Assert.NotNull(context);
        Assert.False(context.Truncated);
        Assert.Equal(["entity:root", "entity:child", "entity:related"],
            context.Entities.Select(item => item.Entity.Id).ToArray());
        var related = Assert.Single(context.Entities, item => item.Entity.Id == "entity:related");
        Assert.Equal(1, related.Depth);
        Assert.Contains(SemanticContextConnectionKind.Child, related.Connections);
        Assert.Contains(SemanticContextConnectionKind.Relationship, related.Connections);
        Assert.Contains(SemanticContextConnectionKind.ChildDiagram,
            Assert.Single(context.Entities, item => item.Entity.Id == "entity:child").Connections);
        Assert.Equal("relationship:depends", Assert.Single(context.Relationships).Id);
        Assert.Equal(["diagram:child", "diagram:main"], context.Diagrams.Select(diagram => diagram.Id).ToArray());
        Assert.Equal("asset:diagram", Assert.Single(context.ReferencedAssets).Id);
    }

    /// <summary>Context reports truncation without returning more than the caller's explicit bound.</summary>
    [Fact]
    public void FindEntityContextHonorsEntityBound()
    {
        var context = SemanticWorkspaceReadModelService.FindEntityContext(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "entity:root",
            depth: 2,
            maximumEntities: 2);

        Assert.NotNull(context);
        Assert.True(context.Truncated);
        Assert.Equal(2, context.Entities.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SemanticWorkspaceReadModelService.FindEntityContext(
                SemanticWorkspaceReadModelTestData.CreateWorkspace(),
                "entity:root",
                SemanticWorkspaceReadModelService.MaximumDepth + 1));
    }

    /// <summary>Architecture-node context resolves every semantic binding without inventing architecture DTOs.</summary>
    [Fact]
    public void FindArchitectureNodeContextsReturnsBoundSemanticEntities()
    {
        var contexts = SemanticWorkspaceReadModelService.FindArchitectureNodeContexts(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "architecture:application",
            depth: 0);

        Assert.Equal("entity:root", Assert.Single(contexts).RootEntityId);
        Assert.Empty(SemanticWorkspaceReadModelService.FindArchitectureNodeContexts(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            "architecture:missing"));
    }
}

internal static class SemanticWorkspaceReadModelTestData
{
    public static Cave.Domain.SemanticWorkspace CreateWorkspace()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["z"] = "last",
            ["a"] = "first",
        };
        var tracking = new SemanticTrackingFacet(
            "board:delivery",
            "column:doing",
            new SemanticProgress(SemanticProgressMode.Manual, 45),
            metadata);
        var main = new SemanticDiagram(
            "diagram:main",
            "Main architecture",
            "Coordinates the delivery slice.",
            null,
            tracking,
            new SemanticDiagramView(
                1.25,
                10,
                20,
                [new SemanticEntityPresentation("entity:root", 30, 40, 240, 160, false, true, "primary")]),
            metadata);
        var child = new SemanticDiagram(
            "diagram:child",
            "Detailed design",
            "Explains the root component.",
            "diagram:main",
            null,
            new SemanticDiagramView(null, null, null, []),
            new Dictionary<string, string>());
        var unrelated = new SemanticDiagram(
            "diagram:unrelated",
            "Unrelated",
            null,
            null,
            null,
            new SemanticDiagramView(null, null, null, []),
            new Dictionary<string, string>());

        var blocks = new SemanticContentBlock[]
        {
            new SemanticTextBlock("block:text", "Root **purpose**."),
            new SemanticChecklistBlock("block:checklist",
            [
                new SemanticChecklistItem("item:second", "Second task", false, 2),
                new SemanticChecklistItem("item:first", "First task", true, 1),
            ]),
            new SemanticCodeBlock("block:code", "public sealed class Example;", "csharp", "Example.cs"),
            new SemanticMathBlock("block:math", "E = mc^2"),
            new SemanticImageBlock("block:image", "asset:diagram", "System context", "Architecture diagram"),
            new SemanticDrawingBlock("block:drawing",
            [
                new SemanticDrawingStroke("stroke:one",
                [
                    new SemanticDrawingPoint(0, 0, 0.5),
                    new SemanticDrawingPoint(1, 1, 0.8),
                ], "#123456", 2),
            ]),
        };
        var root = new SemanticEntity(
            "entity:root",
            "diagram:main",
            "component",
            "Application",
            "Owns use cases.",
            null,
            "diagram:child",
            "architecture:application",
            blocks,
            tracking,
            ["application", "critical"],
            metadata);
        var related = new SemanticEntity(
            "entity:related",
            "diagram:main",
            "component",
            "Domain",
            null,
            "entity:root",
            null,
            null,
            [],
            null,
            [],
            new Dictionary<string, string>());
        var childEntity = new SemanticEntity(
            "entity:child",
            "diagram:child",
            "decision",
            "Boundary decision",
            null,
            null,
            null,
            null,
            [],
            null,
            [],
            new Dictionary<string, string>());
        var unrelatedEntity = new SemanticEntity(
            "entity:unrelated",
            "diagram:unrelated",
            "note",
            "Unrelated note",
            null,
            null,
            null,
            null,
            [],
            null,
            [],
            new Dictionary<string, string>());
        var relation = new SemanticRelationship(
            "relationship:depends",
            "diagram:main",
            "entity:root",
            "entity:related",
            "depends-on",
            "uses domain",
            metadata);
        var board = new SemanticKanbanBoard(
            "board:delivery",
            "Delivery",
            [
                new SemanticKanbanColumn("column:done", "Done", 2, "green"),
                new SemanticKanbanColumn("column:doing", "Doing", 1, "amber"),
            ],
            metadata);
        var asset = new SemanticAssetReference(
            "asset:diagram",
            "assets/diagram.png",
            "image/png",
            "Architecture",
            "Architecture diagram",
            metadata);

        return new Cave.Domain.SemanticWorkspace(
            SemanticWorkspace.CurrentSchemaVersion,
            "workspace:engineering",
            "Engineering workspace",
            7,
            new DateTimeOffset(2026, 8, 30, 6, 30, 0, TimeSpan.Zero),
            [unrelated, child, main],
            [unrelatedEntity, childEntity, related, root],
            [relation],
            [board],
            [asset]);
    }
}
