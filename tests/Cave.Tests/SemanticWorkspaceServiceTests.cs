using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Tests;

/// <summary>Verifies semantic-workspace validation, atomic batches, provenance, and history.</summary>
public sealed class SemanticWorkspaceServiceTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Verifies that a missing file projects as a stable empty schema-v1 workspace.</summary>
    [Fact]
    public async Task GetAsyncReturnsDeterministicEmptyWorkspaceWhenStateIsAbsent()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");

        var first = await service.GetAsync(root, CancellationToken.None);
        var second = await service.GetAsync(root, CancellationToken.None);

        Assert.Equal(SemanticWorkspaceModel.CurrentSchemaVersion, first.SchemaVersion);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(0, first.Revision);
        Assert.Empty(first.Diagrams);
        Assert.Equal(0, store.SaveCount);
    }

    /// <summary>Verifies a multi-object batch commits once and returns every accepted/generated create ID.</summary>
    [Fact]
    public async Task ApplyOperationsAsyncCommitsRichTrackedModelAtomically()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var tracking = new SemanticTrackingFacet(
            "board:work",
            "column:todo",
            new SemanticProgress(SemanticProgressMode.Manual, 25),
            EmptyMetadata);
        var entity = Entity(
            "entity:feature",
            "diagram:root",
            [
                new SemanticTextBlock("", "Feature context"),
                new SemanticChecklistBlock(
                    "block:checklist",
                    [new SemanticChecklistItem("", "First step", false, 0)]),
                new SemanticImageBlock("block:image", "asset:diagram", null, "Architecture sketch"),
                new SemanticDrawingBlock(
                    "block:drawing",
                    [new SemanticDrawingStroke("", [new SemanticDrawingPoint(0, 0, 0.5)], "#008080", 2)]),
            ]) with { Tracking = tracking };
        var batch = new SemanticWorkspaceBatch(
            new SemanticActor(SemanticActorType.Codex, "agent:1", "Codex"),
            "Create one tracked feature slice",
            [
                new CreateBoardOperation(new SemanticKanbanBoard(
                    "board:work",
                    "Work",
                    [
                        new SemanticKanbanColumn("column:todo", "To do", 0, null),
                        new SemanticKanbanColumn("column:done", "Done", 1, null),
                    ],
                    EmptyMetadata)),
                new CreateDiagramOperation(Diagram("diagram:root", "Root")),
                new CreateAssetReferenceOperation(new SemanticAssetReference(
                    "asset:diagram",
                    "assets/diagram.png",
                    "image/png",
                    null,
                    "Architecture sketch",
                    EmptyMetadata)),
                new CreateEntityOperation(entity),
                new CreateEntityOperation(Entity("entity:owner", "diagram:root", [])),
                new CreateRelationshipOperation(new SemanticRelationship(
                    "relationship:owner",
                    "diagram:root",
                    "entity:feature",
                    "entity:owner",
                    "depends-on",
                    null,
                    EmptyMetadata)),
            ]);

        var result = await service.ApplyOperationsAsync(root, batch, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Workspace.Revision);
        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(result.Transaction);
        Assert.Equal(6, result.Transaction.OperationCount);
        Assert.Contains("entity:feature", result.Transaction.AffectedIds);
        Assert.Contains("entity:feature", result.CreatedIds);
        Assert.All(result.CreatedIds, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.True(result.CreatedIds.Count >= 12);
        Assert.Same(store.State, result.Workspace);
    }

    /// <summary>Verifies final aggregate validation rejects the complete batch without persisting earlier operations.</summary>
    [Fact]
    public async Task ApplyOperationsAsyncRejectsWholeBatchWithAttributedStructuredError()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var batch = new SemanticWorkspaceBatch(
            new SemanticActor(SemanticActorType.Human, "operator", "Operator"),
            "Attempt an invalid cross-diagram object",
            [
                new CreateDiagramOperation(Diagram("diagram:root", "Root")),
                new CreateEntityOperation(Entity("entity:orphan", "diagram:missing", [])),
            ]);

        var result = await service.ApplyOperationsAsync(root, batch, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, store.SaveCount);
        var error = Assert.Single(result.Errors, item => item.Code == "entity_diagram_missing");
        Assert.Equal(1, error.OperationIndex);
        Assert.Equal("diagram:missing", error.TargetId);
        Assert.Contains("diagram:root", error.Alternatives);
        Assert.Empty(result.Workspace.Entities);
    }

    /// <summary>Verifies one committed batch is one undo step and revisions remain monotonic through redo.</summary>
    [Fact]
    public async Task UndoAndRedoRestoreOneWholeBatchWithMonotonicRevision()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var actor = new SemanticActor(SemanticActorType.Human, "operator", "Operator");
        var applied = await service.ApplyOperationsAsync(
            root,
            new SemanticWorkspaceBatch(
                actor,
                "Create diagram and entity",
                [
                    new CreateDiagramOperation(Diagram("diagram:root", "Root")),
                    new CreateEntityOperation(Entity("entity:one", "diagram:root", [])),
                ]),
            CancellationToken.None);

        var undone = await service.UndoAsync(root, actor, CancellationToken.None);
        var redone = await service.RedoAsync(root, actor, CancellationToken.None);

        Assert.True(applied.Succeeded);
        Assert.True(undone.Succeeded);
        Assert.True(redone.Succeeded);
        Assert.Equal(1, applied.Workspace.Revision);
        Assert.Equal(2, undone.Workspace.Revision);
        Assert.Empty(undone.Workspace.Diagrams);
        Assert.Equal(3, redone.Workspace.Revision);
        Assert.Single(redone.Workspace.Diagrams);
        Assert.Single(redone.Workspace.Entities);
        Assert.Equal(3, store.SaveCount);
    }

    /// <summary>Verifies deleting a positioned entity removes its owned view record and supports undo.</summary>
    [Fact]
    public async Task DeleteEntityRemovesPresentationWithoutDeletingOtherLayout()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var actor = new SemanticActor(SemanticActorType.Human, "operator", "Operator");
        var diagram = Diagram("diagram:root", "Root") with
        {
            View = new SemanticDiagramView(null, null, null,
            [
                new SemanticEntityPresentation("entity:one", 10, 20, null, null, false, true, null),
                new SemanticEntityPresentation("entity:two", 30, 40, null, null, false, true, null),
            ]),
        };
        var applied = await service.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(
            actor, "Create two positioned entities",
            [
                new CreateDiagramOperation(diagram),
                new CreateEntityOperation(Entity("entity:one", diagram.Id, [])),
                new CreateEntityOperation(Entity("entity:two", diagram.Id, [])),
            ]), CancellationToken.None);
        Assert.True(applied.Succeeded);

        var deleted = await service.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(
            actor, "Delete positioned entity", [new DeleteEntityOperation("entity:one")]),
            CancellationToken.None);
        Assert.True(deleted.Succeeded);
        Assert.Equal("entity:two", Assert.Single(deleted.Workspace.Entities).Id);
        Assert.Equal("entity:two", Assert.Single(Assert.Single(deleted.Workspace.Diagrams).View.Entities).EntityId);
        Assert.Contains(diagram.Id, deleted.Transaction!.AffectedIds);

        var undone = await service.UndoAsync(root, actor, CancellationToken.None);
        Assert.True(undone.Succeeded);
        Assert.Equal(2, undone.Workspace.Entities.Count);
        Assert.Equal(2, Assert.Single(undone.Workspace.Diagrams).View.Entities.Count);
    }

    /// <summary>Verifies multiple undo and redo steps retain history while advancing durable revisions.</summary>
    [Fact]
    public async Task UndoAndRedoTraverseMultipleBatchesWithMonotonicRevision()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var actor = new SemanticActor(SemanticActorType.Human, "operator", "Operator");
        long revision = 0;
        for (var index = 1; index <= 3; index++)
        {
            var applied = await service.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(
                actor,
                $"Create diagram {index}",
                [new CreateDiagramOperation(Diagram($"diagram:{index}", $"Diagram {index}"))]),
                CancellationToken.None);
            Assert.True(applied.Succeeded);
            Assert.Equal(++revision, applied.Workspace.Revision);
        }

        for (var count = 2; count >= 0; count--)
        {
            var undone = await service.UndoAsync(root, actor, CancellationToken.None);
            Assert.True(undone.Succeeded);
            Assert.Equal(++revision, undone.Workspace.Revision);
            Assert.Equal(count, undone.Workspace.Diagrams.Count);
        }

        for (var count = 1; count <= 3; count++)
        {
            var redone = await service.RedoAsync(root, actor, CancellationToken.None);
            Assert.True(redone.Succeeded);
            Assert.Equal(++revision, redone.Workspace.Revision);
            Assert.Equal(count, redone.Workspace.Diagrams.Count);
        }

        var anotherUndo = await service.UndoAsync(root, actor, CancellationToken.None);
        var anotherRedo = await service.RedoAsync(root, actor, CancellationToken.None);
        Assert.True(anotherUndo.Succeeded);
        Assert.Equal(++revision, anotherUndo.Workspace.Revision);
        Assert.Equal(2, anotherUndo.Workspace.Diagrams.Count);
        Assert.True(anotherRedo.Succeeded);
        Assert.Equal(++revision, anotherRedo.Workspace.Revision);
        Assert.Equal(3, anotherRedo.Workspace.Diagrams.Count);
    }

    /// <summary>Verifies external commits invalidate history without overwriting their changes.</summary>
    [Fact]
    public async Task UndoRejectsExternalRevisionAndNewBatchStartsFreshHistory()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var externalService = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var actor = new SemanticActor(SemanticActorType.Human, "operator", "Operator");
        async Task Apply(SemanticWorkspaceService owner, string id)
        {
            var result = await owner.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(
                actor, $"Create {id}", [new CreateDiagramOperation(Diagram(id, id))]),
                CancellationToken.None);
            Assert.True(result.Succeeded);
        }

        await Apply(service, "diagram:local");
        await Apply(externalService, "diagram:external");
        var conflict = await service.UndoAsync(root, actor, CancellationToken.None);
        Assert.False(conflict.Succeeded);
        Assert.Contains(conflict.Errors, error => error.Code == "history_revision_conflict");
        Assert.Equal(2, conflict.Workspace.Diagrams.Count);
        Assert.Equal(2, store.SaveCount);

        await Apply(service, "diagram:local-again");
        await Apply(externalService, "diagram:external-again");
        await Apply(service, "diagram:latest");
        var undone = await service.UndoAsync(root, actor, CancellationToken.None);
        Assert.True(undone.Succeeded);
        Assert.Equal(4, undone.Workspace.Diagrams.Count);
        Assert.Contains(undone.Workspace.Diagrams, diagram => diagram.Id == "diagram:external-again");
        var unavailable = await service.UndoAsync(root, actor, CancellationToken.None);
        Assert.False(unavailable.Succeeded);
        Assert.Contains(unavailable.Errors, error => error.Code == "undo_unavailable");
        Assert.Equal(4, unavailable.Workspace.Diagrams.Count);
    }

    /// <summary>Verifies malformed required collections and records cannot durably corrupt semantic state.</summary>
    [Theory]
    [InlineData("entity-blocks")]
    [InlineData("entity-tags")]
    [InlineData("entity-block-entry")]
    [InlineData("board-columns")]
    [InlineData("board-column-entry")]
    [InlineData("view-entries")]
    [InlineData("view-entry")]
    [InlineData("checklist-items")]
    [InlineData("checklist-item-entry")]
    [InlineData("drawing-strokes")]
    [InlineData("drawing-stroke-entry")]
    [InlineData("drawing-points")]
    [InlineData("drawing-point-entry")]
    [InlineData("tracking-progress")]
    [InlineData("entity-object")]
    [InlineData("diagram-object")]
    [InlineData("block-object")]
    [InlineData("create-null-blocks")]
    public async Task ApplyOperationsRejectsMalformedStructureWithoutDurableChange(string invalidField)
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var root = Path.GetFullPath("C:\\semantic-workspace-tests");
        var actor = new SemanticActor(SemanticActorType.Human, "operator", "Operator");
        var diagram = Diagram("diagram:root", "Root");
        var entity = Entity("entity:one", diagram.Id, []);
        var board = new SemanticKanbanBoard("board:work", "Work", [], EmptyMetadata);
        var applied = await service.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(actor, "Create valid state",
        [new CreateDiagramOperation(diagram), new CreateEntityOperation(entity), new CreateBoardOperation(board)]),
            CancellationToken.None);
        Assert.True(applied.Succeeded);
        SemanticWorkspaceOperation operation = invalidField switch
        {
            "entity-blocks" => new UpdateEntityOperation(entity with { Blocks = null! }),
            "entity-tags" => new UpdateEntityOperation(entity with { Tags = null! }),
            "entity-block-entry" => new UpdateEntityOperation(entity with { Blocks = [null!] }),
            "board-columns" => new UpdateBoardOperation(board with { Columns = null! }),
            "board-column-entry" => new UpdateBoardOperation(board with { Columns = [null!] }),
            "view-entries" => new UpdateDiagramViewOperation(diagram.Id, diagram.View with { Entities = null! }),
            "view-entry" => new UpdateDiagramViewOperation(diagram.Id, diagram.View with { Entities = [null!] }),
            "checklist-items" => new AddContentBlockOperation(entity.Id, new SemanticChecklistBlock("block:one", null!)),
            "checklist-item-entry" => new AddContentBlockOperation(entity.Id, new SemanticChecklistBlock("block:one", [null!])),
            "drawing-strokes" => new AddContentBlockOperation(entity.Id, new SemanticDrawingBlock("block:one", null!)),
            "drawing-stroke-entry" => new AddContentBlockOperation(entity.Id, new SemanticDrawingBlock("block:one", [null!])),
            "drawing-points" => new AddContentBlockOperation(entity.Id, new SemanticDrawingBlock("block:one", [new SemanticDrawingStroke("stroke:one", null!, "#000000", 1)])),
            "drawing-point-entry" => new AddContentBlockOperation(entity.Id, new SemanticDrawingBlock("block:one", [new SemanticDrawingStroke("stroke:one", [null!], "#000000", 1)])),
            "tracking-progress" => new SetEntityTrackingOperation(entity.Id, new SemanticTrackingFacet(board.Id, "column:one", null!, EmptyMetadata)),
            "entity-object" => new UpdateEntityOperation(null!),
            "diagram-object" => new CreateDiagramOperation(null!),
            "block-object" => new AddContentBlockOperation(entity.Id, null!),
            "create-null-blocks" => new CreateEntityOperation(entity with { Id = "entity:two", Blocks = null! }),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        var rejected = await service.ApplyOperationsAsync(root, new SemanticWorkspaceBatch(actor, "Reject malformed state",
        [new CreateDiagramOperation(Diagram("diagram:must-not-commit", "Uncommitted")), operation]), CancellationToken.None);

        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, error => error.OperationIndex == 1
            && error.Code is "collection_required" or "collection_entry_required" or "object_required");
        Assert.Equal(1, store.SaveCount);
        Assert.Same(applied.Workspace, store.State);
        Assert.Same(applied.Workspace, await service.GetAsync(root, CancellationToken.None));
    }

    /// <summary>Verifies malformed persisted collections fail validation before read projections can dereference them.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatorRejectsNullPersistedWorkspaceCollections(bool nullEntry)
    {
        var workspace = SemanticWorkspaceModel.Empty("workspace:test", "Test", DateTimeOffset.UtcNow) with
        {
            Entities = nullEntry ? [null!] : null!,
        };

        var errors = SemanticWorkspaceValidator.Validate(workspace);

        Assert.Contains(errors, error => error.Code == (nullEntry ? "collection_entry_required" : "collection_required"));
    }

    /// <summary>Verifies operation and content discriminators are owned by one canonical JSON contract.</summary>
    [Fact]
    public void CanonicalJsonRoundTripsPolymorphicOperationAndContentBlock()
    {
        SemanticWorkspaceOperation operation = new AddContentBlockOperation(
            "entity:one",
            new SemanticTextBlock("block:text", "Hello"));
        var options = SemanticWorkspaceJson.CreateOptions();

        var json = JsonSerializer.Serialize(operation, options);
        var roundTrip = JsonSerializer.Deserialize<SemanticWorkspaceOperation>(json, options);

        Assert.Contains("\"operation\":\"add-content-block\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"text\"", json, StringComparison.Ordinal);
        var add = Assert.IsType<AddContentBlockOperation>(roundTrip);
        Assert.Equal("Hello", Assert.IsType<SemanticTextBlock>(add.Block).Text);
    }

    /// <summary>Verifies parent and child-diagram cycles are rejected centrally.</summary>
    [Fact]
    public void ValidatorRejectsParentAndChildDiagramCycles()
    {
        var workspace = SemanticWorkspaceModel.Empty("workspace:test", "Test", DateTimeOffset.UtcNow) with
        {
            Diagrams =
            [
                Diagram("diagram:a", "A") with { ParentDiagramId = "diagram:b" },
                Diagram("diagram:b", "B") with { ParentDiagramId = "diagram:a" },
            ],
            Entities =
            [
                Entity("entity:a", "diagram:a", []) with { ChildDiagramId = "diagram:b" },
                Entity("entity:b", "diagram:b", []) with { ChildDiagramId = "diagram:a" },
            ],
        };

        var errors = SemanticWorkspaceValidator.Validate(workspace);

        Assert.Contains(errors, item => item.Code == "diagram_parent_cycle");
        Assert.Contains(errors, item => item.Code == "child_diagram_cycle");
    }

    /// <summary>Verifies stable IDs remain unambiguous across every object family.</summary>
    [Fact]
    public void ValidatorRejectsStableIdsReusedAcrossObjectFamilies()
    {
        var workspace = SemanticWorkspaceModel.Empty("workspace:test", "Test", DateTimeOffset.UtcNow) with
        {
            Diagrams = [Diagram("shared:id", "Root")],
            Boards = [new SemanticKanbanBoard("shared:id", "Work", [], EmptyMetadata)],
        };

        var error = Assert.Single(
            SemanticWorkspaceValidator.Validate(workspace),
            item => item.Code == "duplicate_id" && item.TargetId == "shared:id");

        Assert.Contains("workspace object", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Verifies an unattributed transport batch is rejected before persistence.</summary>
    [Fact]
    public async Task ApplyOperationsAsyncRejectsMissingActor()
    {
        var store = new RecordingStore();
        var service = new SemanticWorkspaceService(store, TimeProvider.System);
        var batch = new SemanticWorkspaceBatch(
            null!,
            "Create a diagram",
            [new CreateDiagramOperation(Diagram("diagram:root", "Root"))]);

        var result = await service.ApplyOperationsAsync(
            Path.GetFullPath("C:\\semantic-workspace-tests"),
            batch,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, item => item.Code == "transaction_actor_required");
        Assert.Equal(0, store.SaveCount);
    }

    private static SemanticDiagram Diagram(string id, string title) => new(
        id,
        title,
        null,
        null,
        null,
        new SemanticDiagramView(null, null, null, []),
        EmptyMetadata);

    private static SemanticEntity Entity(
        string id,
        string diagramId,
        IReadOnlyList<SemanticContentBlock> blocks) => new(
        id,
        diagramId,
        "task",
        id,
        null,
        null,
        null,
        null,
        blocks,
        null,
        [],
        EmptyMetadata);

    private sealed class RecordingStore : ISemanticWorkspaceStore
    {
        public SemanticWorkspaceModel? State { get; private set; }
        public int SaveCount { get; private set; }

        public Task<SemanticWorkspaceModel?> LoadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(State);
        }

        public Task SaveAsync(
            string workspaceRoot,
            SemanticWorkspaceModel workspace,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actualRevision = State?.Revision ?? 0;
            if (actualRevision != expectedRevision)
            {
                throw new SemanticWorkspaceConcurrencyException(expectedRevision, actualRevision);
            }
            State = workspace;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
