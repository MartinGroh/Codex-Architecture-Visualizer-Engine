using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.SemanticWorkspace;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Tests;

/// <summary>Verifies canonical semantic-workspace JSON persistence and failure behavior.</summary>
public sealed class SemanticWorkspacePersistenceTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Verifies a missing canonical file is an empty-state signal rather than an error.</summary>
    [Fact]
    public async Task LoadAsyncReturnsNullWhenCanonicalFileIsAbsent()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSemanticWorkspaceStore();

        var workspace = await store.LoadAsync(directory.Path, CancellationToken.None);

        Assert.Null(workspace);
    }

    /// <summary>Verifies camel-case schema-v1 JSON round-trips every polymorphic content-block foundation.</summary>
    [Fact]
    public async Task SaveAndLoadRoundTripCanonicalWorkspaceAtomically()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSemanticWorkspaceStore();
        var workspace = CreateWorkspace("Initial");

        await store.SaveAsync(directory.Path, workspace, expectedRevision: 0, CancellationToken.None);
        var loaded = await store.LoadAsync(directory.Path, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(workspace.Id, loaded.Id);
        var blocks = Assert.Single(loaded.Entities).Blocks;
        Assert.Collection(
            blocks,
            block => Assert.IsType<SemanticTextBlock>(block),
            block => Assert.IsType<SemanticChecklistBlock>(block),
            block => Assert.IsType<SemanticCodeBlock>(block),
            block => Assert.IsType<SemanticMathBlock>(block),
            block => Assert.IsType<SemanticImageBlock>(block),
            block => Assert.IsType<SemanticDrawingBlock>(block));
        var path = System.IO.Path.Combine(directory.Path, ".cave", "semantic-workspace.json");
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"drawing\"", json, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(path)!, "*.tmp"));
    }

    /// <summary>Verifies separate host/MCP store instances cannot both replace the same base revision.</summary>
    [Fact]
    public async Task ConcurrentWritersRejectTheStaleCandidateInsteadOfLosingAnUpdate()
    {
        using var directory = new TemporaryDirectory();
        var firstStore = new FileSemanticWorkspaceStore();
        var secondStore = new FileSemanticWorkspaceStore();

        var attempts = await Task.WhenAll(
            Record.ExceptionAsync(() => firstStore.SaveAsync(
                directory.Path,
                CreateWorkspace("First"),
                expectedRevision: 0,
                CancellationToken.None)),
            Record.ExceptionAsync(() => secondStore.SaveAsync(
                directory.Path,
                CreateWorkspace("Second"),
                expectedRevision: 0,
                CancellationToken.None)));

        Assert.Single(attempts, exception => exception is null);
        var conflict = Assert.Single(attempts.OfType<SemanticWorkspaceConcurrencyException>());
        Assert.Equal(0, conflict.ExpectedRevision);
        Assert.Equal(1, conflict.ActualRevision);
        var loaded = await firstStore.LoadAsync(directory.Path, CancellationToken.None);
        Assert.True(loaded?.Name is "First" or "Second");
    }

    /// <summary>Verifies an unsupported future version fails clearly without dropping fields.</summary>
    [Fact]
    public async Task LoadAsyncRejectsUnsupportedFutureSchema()
    {
        using var directory = new TemporaryDirectory();
        var stateDirectory = System.IO.Path.Combine(directory.Path, ".cave");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(stateDirectory, "semantic-workspace.json"),
            "{\"schemaVersion\":99,\"futureField\":true}");
        var store = new FileSemanticWorkspaceStore();

        var exception = await Assert.ThrowsAsync<UnsupportedSemanticWorkspaceSchemaException>(
            () => store.LoadAsync(directory.Path, CancellationToken.None));

        Assert.Equal(99, exception.ActualVersion);
        Assert.Equal(SemanticWorkspaceModel.CurrentSchemaVersion, exception.SupportedVersion);
    }

    /// <summary>Verifies malformed JSON remains a visible persistence failure.</summary>
    [Fact]
    public async Task LoadAsyncReportsMalformedCanonicalJson()
    {
        using var directory = new TemporaryDirectory();
        var stateDirectory = System.IO.Path.Combine(directory.Path, ".cave");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(stateDirectory, "semantic-workspace.json"),
            "{not-json");
        var store = new FileSemanticWorkspaceStore();

        var exception = await Assert.ThrowsAsync<SemanticWorkspacePersistenceException>(
            () => store.LoadAsync(directory.Path, CancellationToken.None));

        Assert.Contains("invalid JSON", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies cancellation before replacement preserves the prior canonical document.</summary>
    [Fact]
    public async Task CancelledSavePreservesPriorCanonicalWorkspace()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSemanticWorkspaceStore();
        await store.SaveAsync(directory.Path, CreateWorkspace("Before"), expectedRevision: 0, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(directory.Path, CreateWorkspace("After"), expectedRevision: 1, cancellation.Token));
        var loaded = await store.LoadAsync(directory.Path, CancellationToken.None);

        Assert.Equal("Before", loaded?.Name);
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(directory.Path, ".cave"),
            "*.tmp"));
    }

    private static SemanticWorkspaceModel CreateWorkspace(string name)
    {
        var diagram = new SemanticDiagram(
            "diagram:root",
            "Root",
            null,
            null,
            null,
            new SemanticDiagramView(null, null, null, []),
            EmptyMetadata);
        var asset = new SemanticAssetReference(
            "asset:image",
            "assets/image.png",
            "image/png",
            null,
            "Image",
            EmptyMetadata);
        var blocks = new SemanticContentBlock[]
        {
            new SemanticTextBlock("block:text", "Text"),
            new SemanticChecklistBlock(
                "block:checklist",
                [new SemanticChecklistItem("item:one", "One", false, 0)]),
            new SemanticCodeBlock("block:code", "var value = 1;", "csharp", "Example.cs"),
            new SemanticMathBlock("block:math", "x^2"),
            new SemanticImageBlock("block:image", asset.Id, null, "Image"),
            new SemanticDrawingBlock(
                "block:drawing",
                [new SemanticDrawingStroke(
                    "stroke:one",
                    [new SemanticDrawingPoint(1, 2, 0.5)],
                    "#000000",
                    1)]),
        };
        var entity = new SemanticEntity(
            "entity:one",
            diagram.Id,
            "note",
            "One",
            null,
            null,
            null,
            null,
            blocks,
            null,
            [],
            EmptyMetadata);
        return new SemanticWorkspaceModel(
            SemanticWorkspaceModel.CurrentSchemaVersion,
            "workspace:test",
            name,
            1,
            DateTimeOffset.UtcNow,
            [diagram],
            [entity],
            [],
            [],
            [asset]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cave-semantic-workspace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
