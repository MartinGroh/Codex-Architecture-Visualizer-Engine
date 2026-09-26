using System.Text.Json;
using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>Verifies canonical semantic JSON and human-readable export projections.</summary>
public sealed class SemanticWorkspaceExportTests
{
    /// <summary>JSON is versioned, import-ready, polymorphic, and presentation-free by default.</summary>
    [Fact]
    public void ExportJsonUsesVersionedSemanticShapeAndOmitsViewByDefault()
    {
        var workspace = SemanticWorkspaceReadModelTestData.CreateWorkspace();

        var json = SemanticWorkspaceExportService.ExportJson(
            workspace,
            new SemanticWorkspaceExportOptions(WriteIndented: false));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("cave-semantic-workspace", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        var payload = root.GetProperty("workspace");
        Assert.Equal(1, payload.GetProperty("schemaVersion").GetInt32());
        Assert.False(payload.GetProperty("diagrams")[0].TryGetProperty("view", out _));

        var rootEntity = payload.GetProperty("entities")
            .EnumerateArray()
            .Single(entity => entity.GetProperty("id").GetString() == "entity:root");
        var blocks = rootEntity.GetProperty("blocks").EnumerateArray().ToArray();
        Assert.Equal(["text", "checklist", "code", "math", "image", "drawing"],
            blocks.Select(block => block.GetProperty("kind").GetString()!).ToArray());
        Assert.Equal("Manual", rootEntity.GetProperty("tracking").GetProperty("progress").GetProperty("mode").GetString());
        Assert.Equal("asset:diagram", blocks[4].GetProperty("assetId").GetString());
        Assert.Equal(2, blocks[5].GetProperty("strokes")[0].GetProperty("points").GetArrayLength());
    }

    /// <summary>Presentation state is emitted only after the caller explicitly requests it.</summary>
    [Fact]
    public void ExportJsonIncludesViewOnlyWhenRequested()
    {
        var json = SemanticWorkspaceExportService.ExportJson(
            SemanticWorkspaceReadModelTestData.CreateWorkspace(),
            new SemanticWorkspaceExportOptions(IncludePresentationState: true, WriteIndented: false));

        using var document = JsonDocument.Parse(json);
        var diagrams = document.RootElement.GetProperty("workspace").GetProperty("diagrams");
        var main = diagrams.EnumerateArray().Single(diagram => diagram.GetProperty("id").GetString() == "diagram:main");

        Assert.Equal(1.25, main.GetProperty("view").GetProperty("zoom").GetDouble());
        Assert.Equal("entity:root", main.GetProperty("view").GetProperty("entities")[0].GetProperty("entityId").GetString());
    }

    /// <summary>Unordered canonical collections and metadata produce byte-identical compact JSON.</summary>
    [Fact]
    public void ExportJsonIsDeterministicAcrossCollectionAndMetadataOrder()
    {
        var first = SemanticWorkspaceReadModelTestData.CreateWorkspace();
        var second = first with
        {
            Diagrams = first.Diagrams.Reverse().ToArray(),
            Entities = first.Entities.Reverse().ToArray(),
            Relationships = first.Relationships.Reverse().ToArray(),
            Boards = first.Boards.Reverse().ToArray(),
            Assets = first.Assets.Reverse().ToArray(),
        };
        var options = new SemanticWorkspaceExportOptions(WriteIndented: false);

        var firstJson = SemanticWorkspaceExportService.ExportJson(first, options);
        var secondJson = SemanticWorkspaceExportService.ExportJson(second, options);

        Assert.Equal(firstJson, secondJson);
        Assert.True(firstJson.IndexOf("\"a\":\"first\"", StringComparison.Ordinal) <
                    firstJson.IndexOf("\"z\":\"last\"", StringComparison.Ordinal));
    }

    /// <summary>Markdown preserves rich semantic content, references, tracking, and typed relations.</summary>
    [Fact]
    public void ExportHumanReadablePreservesSemanticMeaning()
    {
        var markdown = SemanticWorkspaceExportService.ExportHumanReadable(
            SemanticWorkspaceReadModelTestData.CreateWorkspace());

        Assert.Contains("# Engineering workspace", markdown, StringComparison.Ordinal);
        Assert.Contains("- [x] First task", markdown, StringComparison.Ordinal);
        Assert.Contains("```csharp", markdown, StringComparison.Ordinal);
        Assert.Contains("```math", markdown, StringComparison.Ordinal);
        Assert.Contains("![Architecture diagram](assets/diagram.png)", markdown, StringComparison.Ordinal);
        Assert.Contains("`entity:root` --depends-on--> `entity:related`", markdown, StringComparison.Ordinal);
        Assert.Contains("Child diagram: `diagram:child`", markdown, StringComparison.Ordinal);
        Assert.Contains("Tracking boards", markdown, StringComparison.Ordinal);
    }
}
