using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Cave.Domain;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Application;

/// <summary>
/// Owns validated semantic-workspace reads, atomic typed batches, and revision-checked undo/redo.
/// </summary>
/// <param name="store">The application-owned durable-state port.</param>
/// <param name="timeProvider">The authoritative transaction clock.</param>
public sealed class SemanticWorkspaceService(
    ISemanticWorkspaceStore store,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, WorkspaceRuntime> _runtimes = new(PathComparer);

    /// <summary>Loads the current canonical aggregate or a deterministic empty schema-v1 workspace.</summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The valid current semantic workspace.</returns>
    /// <exception cref="SemanticWorkspaceValidationException">Persisted state violates schema-v1 invariants.</exception>
    public async Task<SemanticWorkspaceModel> GetAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var root = CanonicalizeRoot(workspaceRoot);
        var runtime = _runtimes.GetOrAdd(root, _ => new WorkspaceRuntime());
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadValidatedAsync(root, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    /// <summary>Validates and commits one ordered operation batch as one durable undo step.</summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="batch">The actor-attributed atomic batch.</param>
    /// <param name="cancellationToken">Signals that the commit should stop before replacement.</param>
    /// <returns>The committed workspace and transaction, or structured rejection errors.</returns>
    public async Task<SemanticWorkspaceOperationResult> ApplyOperationsAsync(
        string workspaceRoot,
        SemanticWorkspaceBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var root = CanonicalizeRoot(workspaceRoot);
        var runtime = _runtimes.GetOrAdd(root, _ => new WorkspaceRuntime());
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadValidatedAsync(root, cancellationToken).ConfigureAwait(false);
            if (batch.Actor is null)
            {
                return Failure(current, new SemanticWorkspaceError(
                    "transaction_actor_required",
                    "A semantic transaction requires an attributed actor.",
                    null,
                    current.Id,
                    ["provide actor type and optional actor identity"]));
            }

            if (batch.Operations is null || batch.Operations.Count == 0)
            {
                return Failure(current, new SemanticWorkspaceError(
                    "operations_required",
                    "An atomic batch requires at least one operation.",
                    null,
                    null,
                    ["provide one or more typed operations"]));
            }

            if (string.IsNullOrWhiteSpace(batch.Summary))
            {
                return Failure(current, new SemanticWorkspaceError(
                    "transaction_summary_required",
                    "A semantic transaction requires a concise summary.",
                    null,
                    current.Id,
                    ["describe the batch intent"]));
            }

            if (runtime.HistoryRevision is { } historyRevision && historyRevision != current.Revision)
            {
                runtime.Undo.Clear();
                runtime.Redo.Clear();
                runtime.HistoryRevision = null;
            }

            var mutable = new MutableWorkspace(current);
            for (var index = 0; index < batch.Operations.Count; index++)
            {
                var operation = batch.Operations[index];
                if (operation is null)
                {
                    return Failure(current, new SemanticWorkspaceError(
                        "operation_required",
                        $"Operation {index} is null.",
                        index,
                        null,
                        ["provide a typed operation"]));
                }

                var structureErrors = SemanticWorkspaceValidator.ValidateOperationStructure(operation);
                if (structureErrors.Count > 0)
                {
                    return new SemanticWorkspaceOperationResult(
                        false, current, [], structureErrors.Select(error => error with { OperationIndex = index }).ToArray(), null);
                }

                try
                {
                    ApplyOperation(mutable, operation, index);
                }
                catch (OperationRejectedException exception)
                {
                    return Failure(current, exception.Error with { OperationIndex = index });
                }
            }

            var now = timeProvider.GetUtcNow();
            var candidate = mutable.ToWorkspace(current.Revision + 1, now);
            var errors = SemanticWorkspaceValidator.Validate(candidate, mutable.OperationByTarget);
            if (errors.Count > 0)
            {
                return new SemanticWorkspaceOperationResult(false, current, [], errors, null);
            }

            var transaction = new SemanticWorkspaceTransaction(
                Guid.NewGuid().ToString("N"),
                batch.Actor,
                now,
                batch.Summary.Trim(),
                mutable.AffectedIds.Order(StringComparer.Ordinal).ToArray(),
                batch.Operations.Count);
            try
            {
                await store.SaveAsync(root, candidate, current.Revision, cancellationToken).ConfigureAwait(false);
            }
            catch (SemanticWorkspaceConcurrencyException exception)
            {
                runtime.Undo.Clear();
                runtime.Redo.Clear();
                runtime.HistoryRevision = null;
                var durable = await LoadValidatedAsync(root, cancellationToken).ConfigureAwait(false);
                return Failure(durable, new SemanticWorkspaceError(
                    "revision_conflict",
                    exception.Message,
                    null,
                    durable.Id,
                    ["reload the workspace", "reapply the operation batch to the latest revision"]));
            }

            runtime.Undo.Push(new HistoryEntry(current, candidate, transaction));
            runtime.Redo.Clear();
            runtime.HistoryRevision = candidate.Revision;
            return new SemanticWorkspaceOperationResult(
                true,
                candidate,
                mutable.CreatedIds.ToArray(),
                [],
                transaction);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    /// <summary>Restores the state before the latest service-session batch as one atomic write.</summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="actor">The actor requesting undo.</param>
    /// <param name="cancellationToken">Signals that the write should stop before replacement.</param>
    /// <returns>The restored aggregate or a structured unavailable/conflict error.</returns>
    public Task<SemanticWorkspaceOperationResult> UndoAsync(
        string workspaceRoot,
        SemanticActor actor,
        CancellationToken cancellationToken) =>
        RestoreAsync(workspaceRoot, actor, undo: true, cancellationToken);

    /// <summary>Reapplies the latest service-session batch undone through this service instance.</summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="actor">The actor requesting redo.</param>
    /// <param name="cancellationToken">Signals that the write should stop before replacement.</param>
    /// <returns>The restored aggregate or a structured unavailable/conflict error.</returns>
    public Task<SemanticWorkspaceOperationResult> RedoAsync(
        string workspaceRoot,
        SemanticActor actor,
        CancellationToken cancellationToken) =>
        RestoreAsync(workspaceRoot, actor, undo: false, cancellationToken);

    private async Task<SemanticWorkspaceOperationResult> RestoreAsync(
        string workspaceRoot,
        SemanticActor actor,
        bool undo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var root = CanonicalizeRoot(workspaceRoot);
        var runtime = _runtimes.GetOrAdd(root, _ => new WorkspaceRuntime());
        await runtime.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadValidatedAsync(root, cancellationToken).ConfigureAwait(false);
            var source = undo ? runtime.Undo : runtime.Redo;
            var destination = undo ? runtime.Redo : runtime.Undo;
            if (source.Count == 0)
            {
                return Failure(current, new SemanticWorkspaceError(
                    undo ? "undo_unavailable" : "redo_unavailable",
                    undo
                        ? "No semantic-workspace batch is available to undo in this service session."
                        : "No semantic-workspace batch is available to redo in this service session.",
                    null,
                    current.Id,
                    []));
            }

            var entry = source.Peek();
            var expectedRevision = runtime.HistoryRevision;
            if (current.Revision != expectedRevision)
            {
                source.Clear();
                destination.Clear();
                runtime.HistoryRevision = null;
                return Failure(current, new SemanticWorkspaceError(
                    "history_revision_conflict",
                    $"Semantic workspace revision changed from expected {expectedRevision} to {current.Revision}; session history was cleared without overwriting external work.",
                    null,
                    current.Id,
                    ["reload the workspace", "commit a new batch"]));
            }

            var now = timeProvider.GetUtcNow();
            var sourceSnapshot = undo ? entry.Before : entry.After;
            var restored = sourceSnapshot with
            {
                Revision = current.Revision + 1,
                UpdatedAtUtc = now,
            };
            var validationErrors = SemanticWorkspaceValidator.Validate(restored);
            if (validationErrors.Count > 0)
            {
                return new SemanticWorkspaceOperationResult(false, current, [], validationErrors, null);
            }

            try
            {
                await store.SaveAsync(root, restored, current.Revision, cancellationToken).ConfigureAwait(false);
            }
            catch (SemanticWorkspaceConcurrencyException exception)
            {
                source.Clear();
                destination.Clear();
                runtime.HistoryRevision = null;
                var durable = await LoadValidatedAsync(root, cancellationToken).ConfigureAwait(false);
                return Failure(durable, new SemanticWorkspaceError(
                    "history_revision_conflict",
                    exception.Message,
                    null,
                    durable.Id,
                    ["reload the workspace", "commit a new batch"]));
            }
            source.Pop();
            var action = undo ? "Undo" : "Redo";
            var transaction = new SemanticWorkspaceTransaction(
                Guid.NewGuid().ToString("N"),
                actor,
                now,
                $"{action}: {entry.Transaction.Summary}",
                entry.Transaction.AffectedIds,
                entry.Transaction.OperationCount);
            destination.Push(entry);
            // CONSTRAINT: own undo/redo writes advance the durable revision without changing
            // older snapshots; the session cursor keeps all history steps usable while
            // still rejecting any revision written by another service instance.
            runtime.HistoryRevision = restored.Revision;
            return new SemanticWorkspaceOperationResult(true, restored, [], [], transaction);
        }
        finally
        {
            runtime.Gate.Release();
        }
    }

    private async Task<SemanticWorkspaceModel> LoadValidatedAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var workspace = await store.LoadAsync(root, cancellationToken).ConfigureAwait(false)
            ?? CreateEmpty(root, timeProvider.GetUtcNow());
        var errors = SemanticWorkspaceValidator.Validate(workspace);
        if (errors.Count > 0)
        {
            throw new SemanticWorkspaceValidationException(errors);
        }

        return workspace;
    }

    private static SemanticWorkspaceModel CreateEmpty(string root, DateTimeOffset now)
    {
        var identityPath = OperatingSystem.IsWindows() ? root.ToLowerInvariant() : root;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identityPath));
        var id = $"semantic-{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
        return SemanticWorkspaceModel.Empty(id, new DirectoryInfo(root).Name, now);
    }

    private static string CanonicalizeRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException("CAVE requires an absolute semantic-workspace root.", nameof(workspaceRoot));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }

    private static void ApplyOperation(
        MutableWorkspace workspace,
        SemanticWorkspaceOperation operation,
        int operationIndex)
    {
        switch (operation)
        {
            case CreateDiagramOperation create:
            {
                var diagram = create.Diagram with { Id = workspace.CreateId(create.Diagram.Id) };
                workspace.Add(workspace.Diagrams, diagram, item => item.Id, "diagram", operationIndex);
                workspace.Affect(operationIndex, diagram.Id, diagram.ParentDiagramId);
                break;
            }
            case UpdateDiagramOperation update:
                workspace.Replace(workspace.Diagrams, update.Diagram, item => item.Id, "diagram", operationIndex);
                workspace.Affect(operationIndex, update.Diagram.Id, update.Diagram.ParentDiagramId);
                break;
            case DeleteDiagramOperation delete:
                workspace.Remove(workspace.Diagrams, delete.DiagramId, item => item.Id, "diagram", operationIndex);
                workspace.Affect(operationIndex, delete.DiagramId);
                break;
            case CreateEntityOperation create:
            {
                var entity = NormalizeNewEntity(workspace, create.Entity);
                workspace.Add(workspace.Entities, entity, item => item.Id, "entity", operationIndex);
                workspace.Affect(operationIndex, entity.Id, entity.DiagramId, entity.ParentEntityId, entity.ChildDiagramId);
                break;
            }
            case UpdateEntityOperation update:
                workspace.Replace(workspace.Entities, update.Entity, item => item.Id, "entity", operationIndex);
                workspace.Affect(operationIndex, update.Entity.Id, update.Entity.DiagramId, update.Entity.ParentEntityId, update.Entity.ChildDiagramId);
                break;
            case DeleteEntityOperation delete:
                workspace.Remove(workspace.Entities, delete.EntityId, item => item.Id, "entity", operationIndex);
                for (var diagramIndex = 0; diagramIndex < workspace.Diagrams.Count; diagramIndex++)
                {
                    var diagram = workspace.Diagrams[diagramIndex];
                    if (!diagram.View.Entities.Any(item => item.EntityId == delete.EntityId))
                    {
                        continue;
                    }

                    workspace.Diagrams[diagramIndex] = diagram with
                    {
                        View = diagram.View with
                        {
                            Entities = diagram.View.Entities.Where(item => item.EntityId != delete.EntityId).ToArray(),
                        },
                    };
                    workspace.Affect(operationIndex, diagram.Id);
                }
                workspace.Affect(operationIndex, delete.EntityId);
                break;
            case CreateRelationshipOperation create:
            {
                var relationship = create.Relationship with { Id = workspace.CreateId(create.Relationship.Id) };
                workspace.Add(workspace.Relationships, relationship, item => item.Id, "relationship", operationIndex);
                workspace.Affect(operationIndex, relationship.Id, relationship.DiagramId, relationship.SourceEntityId, relationship.TargetEntityId);
                break;
            }
            case UpdateRelationshipOperation update:
                workspace.Replace(workspace.Relationships, update.Relationship, item => item.Id, "relationship", operationIndex);
                workspace.Affect(operationIndex, update.Relationship.Id, update.Relationship.DiagramId, update.Relationship.SourceEntityId, update.Relationship.TargetEntityId);
                break;
            case DeleteRelationshipOperation delete:
                workspace.Remove(workspace.Relationships, delete.RelationshipId, item => item.Id, "relationship", operationIndex);
                workspace.Affect(operationIndex, delete.RelationshipId);
                break;
            case AddContentBlockOperation add:
                MutateBlocks(workspace, add.EntityId, operationIndex, blocks =>
                {
                    var block = NormalizeNewBlock(workspace, add.Block);
                    EnsureAbsent(blocks, block.Id, item => item.Id, "content block");
                    blocks.Add(block);
                    workspace.Affect(operationIndex, block.Id, add.EntityId);
                });
                break;
            case UpdateContentBlockOperation update:
                MutateBlocks(workspace, update.EntityId, operationIndex, blocks =>
                {
                    Replace(blocks, update.Block, item => item.Id, "content block");
                    workspace.Affect(operationIndex, update.Block.Id, update.EntityId);
                });
                break;
            case DeleteContentBlockOperation delete:
                MutateBlocks(workspace, delete.EntityId, operationIndex, blocks =>
                {
                    Remove(blocks, delete.BlockId, item => item.Id, "content block");
                    workspace.Affect(operationIndex, delete.BlockId, delete.EntityId);
                });
                break;
            case AddChecklistItemOperation add:
                MutateChecklist(workspace, add.EntityId, add.BlockId, operationIndex, checklist =>
                {
                    var item = add.Item with { Id = workspace.CreateId(add.Item.Id) };
                    var items = checklist.Items.ToList();
                    EnsureAbsent(items, item.Id, value => value.Id, "checklist item");
                    items.Add(item);
                    workspace.Affect(operationIndex, item.Id, add.BlockId, add.EntityId);
                    return checklist with { Items = items };
                });
                break;
            case UpdateChecklistItemOperation update:
                MutateChecklist(workspace, update.EntityId, update.BlockId, operationIndex, checklist =>
                {
                    var items = checklist.Items.ToList();
                    Replace(items, update.Item, value => value.Id, "checklist item");
                    workspace.Affect(operationIndex, update.Item.Id, update.BlockId, update.EntityId);
                    return checklist with { Items = items };
                });
                break;
            case DeleteChecklistItemOperation delete:
                MutateChecklist(workspace, delete.EntityId, delete.BlockId, operationIndex, checklist =>
                {
                    var items = checklist.Items.ToList();
                    Remove(items, delete.ItemId, value => value.Id, "checklist item");
                    workspace.Affect(operationIndex, delete.ItemId, delete.BlockId, delete.EntityId);
                    return checklist with { Items = items };
                });
                break;
            case ReorderChecklistItemsOperation reorder:
                MutateChecklist(workspace, reorder.EntityId, reorder.BlockId, operationIndex, checklist =>
                {
                    var items = checklist.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
                    if (reorder.ItemIds.Count != items.Count
                        || reorder.ItemIds.Distinct(StringComparer.Ordinal).Count() != items.Count
                        || reorder.ItemIds.Any(id => !items.ContainsKey(id)))
                    {
                        throw Rejected("checklist_reorder_invalid", $"Checklist '{reorder.BlockId}' reorder must contain every item ID exactly once.", reorder.BlockId, items.Keys);
                    }

                    workspace.Affect(
                        operationIndex,
                        new[] { reorder.BlockId, reorder.EntityId }.Concat(reorder.ItemIds).ToArray());
                    return checklist with
                    {
                        Items = reorder.ItemIds.Select((id, order) => items[id] with { Order = order }).ToArray(),
                    };
                });
                break;
            case CreateBoardOperation create:
            {
                var board = NormalizeNewBoard(workspace, create.Board);
                workspace.Add(workspace.Boards, board, item => item.Id, "board", operationIndex);
                workspace.Affect(
                    operationIndex,
                    new[] { board.Id }.Concat(board.Columns.Select(item => item.Id)).ToArray());
                break;
            }
            case UpdateBoardOperation update:
                workspace.Replace(workspace.Boards, update.Board, item => item.Id, "board", operationIndex);
                workspace.Affect(
                    operationIndex,
                    new[] { update.Board.Id }.Concat(update.Board.Columns.Select(item => item.Id)).ToArray());
                break;
            case DeleteBoardOperation delete:
                workspace.Remove(workspace.Boards, delete.BoardId, item => item.Id, "board", operationIndex);
                workspace.Affect(operationIndex, delete.BoardId);
                break;
            case AddBoardColumnOperation add:
                MutateBoard(workspace, add.BoardId, operationIndex, board =>
                {
                    var column = add.Column with { Id = workspace.CreateId(add.Column.Id) };
                    var columns = board.Columns.ToList();
                    EnsureAbsent(columns, column.Id, item => item.Id, "board column");
                    columns.Add(column);
                    workspace.Affect(operationIndex, column.Id, add.BoardId);
                    return board with { Columns = columns };
                });
                break;
            case UpdateBoardColumnOperation update:
                MutateBoard(workspace, update.BoardId, operationIndex, board =>
                {
                    var columns = board.Columns.ToList();
                    Replace(columns, update.Column, item => item.Id, "board column");
                    workspace.Affect(operationIndex, update.Column.Id, update.BoardId);
                    return board with { Columns = columns };
                });
                break;
            case DeleteBoardColumnOperation delete:
                MutateBoard(workspace, delete.BoardId, operationIndex, board =>
                {
                    var columns = board.Columns.ToList();
                    Remove(columns, delete.ColumnId, item => item.Id, "board column");
                    workspace.Affect(operationIndex, delete.ColumnId, delete.BoardId);
                    return board with { Columns = columns };
                });
                break;
            case SetDiagramTrackingOperation tracking:
            {
                var index = IndexOf(workspace.Diagrams, tracking.DiagramId, item => item.Id, "diagram");
                workspace.Diagrams[index] = workspace.Diagrams[index] with { Tracking = tracking.Tracking };
                workspace.Affect(operationIndex, tracking.DiagramId, tracking.Tracking?.BoardId, tracking.Tracking?.ColumnId);
                break;
            }
            case SetEntityTrackingOperation tracking:
            {
                var index = IndexOf(workspace.Entities, tracking.EntityId, item => item.Id, "entity");
                workspace.Entities[index] = workspace.Entities[index] with { Tracking = tracking.Tracking };
                workspace.Affect(operationIndex, tracking.EntityId, tracking.Tracking?.BoardId, tracking.Tracking?.ColumnId);
                break;
            }
            case LinkChildDiagramOperation link:
            {
                var index = IndexOf(workspace.Entities, link.EntityId, item => item.Id, "entity");
                workspace.Entities[index] = workspace.Entities[index] with { ChildDiagramId = link.ChildDiagramId };
                workspace.Affect(operationIndex, link.EntityId, link.ChildDiagramId);
                break;
            }
            case UpdateDiagramViewOperation view:
            {
                var index = IndexOf(workspace.Diagrams, view.DiagramId, item => item.Id, "diagram");
                workspace.Diagrams[index] = workspace.Diagrams[index] with { View = view.View };
                workspace.Affect(
                    operationIndex,
                    new[] { view.DiagramId }.Concat(view.View.Entities.Select(item => item.EntityId)).ToArray());
                break;
            }
            case CreateAssetReferenceOperation create:
            {
                var asset = create.Asset with { Id = workspace.CreateId(create.Asset.Id) };
                workspace.Add(workspace.Assets, asset, item => item.Id, "asset", operationIndex);
                workspace.Affect(operationIndex, asset.Id);
                break;
            }
            case UpdateAssetReferenceOperation update:
                workspace.Replace(workspace.Assets, update.Asset, item => item.Id, "asset", operationIndex);
                workspace.Affect(operationIndex, update.Asset.Id);
                break;
            case DeleteAssetReferenceOperation delete:
                workspace.Remove(workspace.Assets, delete.AssetId, item => item.Id, "asset", operationIndex);
                workspace.Affect(operationIndex, delete.AssetId);
                break;
            default:
                throw Rejected("operation_unsupported", $"Operation '{operation.GetType().Name}' is unsupported.", operation.TargetId, ["use a registered typed operation"]);
        }
    }

    private static SemanticEntity NormalizeNewEntity(MutableWorkspace workspace, SemanticEntity entity) =>
        entity with
        {
            Id = workspace.CreateId(entity.Id),
            Blocks = (entity.Blocks ?? []).Select(block => NormalizeNewBlock(workspace, block)).ToArray(),
        };

    private static SemanticKanbanBoard NormalizeNewBoard(
        MutableWorkspace workspace,
        SemanticKanbanBoard board) =>
        board with
        {
            Id = workspace.CreateId(board.Id),
            Columns = (board.Columns ?? []).Select(column => column with { Id = workspace.CreateId(column.Id) }).ToArray(),
        };

    private static SemanticContentBlock NormalizeNewBlock(
        MutableWorkspace workspace,
        SemanticContentBlock block)
    {
        var id = workspace.CreateId(block.Id);
        return block switch
        {
            SemanticTextBlock value => value with { Id = id },
            SemanticChecklistBlock value => value with
            {
                Id = id,
                Items = (value.Items ?? []).Select(item => item with { Id = workspace.CreateId(item.Id) }).ToArray(),
            },
            SemanticCodeBlock value => value with { Id = id },
            SemanticMathBlock value => value with { Id = id },
            SemanticImageBlock value => value with { Id = id },
            SemanticDrawingBlock value => value with
            {
                Id = id,
                Strokes = (value.Strokes ?? []).Select(stroke => stroke with { Id = workspace.CreateId(stroke.Id) }).ToArray(),
            },
            _ => block,
        };
    }

    private static void MutateBlocks(
        MutableWorkspace workspace,
        string entityId,
        int operationIndex,
        Action<List<SemanticContentBlock>> mutation)
    {
        var index = IndexOf(workspace.Entities, entityId, item => item.Id, "entity");
        var blocks = (workspace.Entities[index].Blocks ?? []).ToList();
        mutation(blocks);
        workspace.Entities[index] = workspace.Entities[index] with { Blocks = blocks };
        workspace.Affect(operationIndex, entityId);
    }

    private static void MutateChecklist(
        MutableWorkspace workspace,
        string entityId,
        string blockId,
        int operationIndex,
        Func<SemanticChecklistBlock, SemanticChecklistBlock> mutation)
    {
        MutateBlocks(workspace, entityId, operationIndex, blocks =>
        {
            var index = IndexOf(blocks, blockId, item => item.Id, "content block");
            if (blocks[index] is not SemanticChecklistBlock checklist)
            {
                throw Rejected("content_block_not_checklist", $"Content block '{blockId}' is not a checklist.", blockId, ["select a checklist block"]);
            }

            blocks[index] = mutation(checklist);
        });
    }

    private static void MutateBoard(
        MutableWorkspace workspace,
        string boardId,
        int operationIndex,
        Func<SemanticKanbanBoard, SemanticKanbanBoard> mutation)
    {
        var index = IndexOf(workspace.Boards, boardId, item => item.Id, "board");
        workspace.Boards[index] = mutation(workspace.Boards[index]);
        workspace.Affect(operationIndex, boardId);
    }

    private static void EnsureAbsent<T>(
        IEnumerable<T> items,
        string id,
        Func<T, string> idSelector,
        string kind)
    {
        if (items.Any(item => idSelector(item).Equals(id, StringComparison.Ordinal)))
        {
            throw Rejected("duplicate_id", $"{kind} ID '{id}' already exists.", id, ["choose a different ID", "omit the create ID to generate one"]);
        }
    }

    private static int IndexOf<T>(
        IReadOnlyList<T> items,
        string id,
        Func<T, string> idSelector,
        string kind)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (idSelector(items[index]).Equals(id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw Rejected("target_not_found", $"{kind} '{id}' does not exist.", id, items.Select(idSelector));
    }

    private static void Replace<T>(
        IList<T> items,
        T replacement,
        Func<T, string> idSelector,
        string kind) =>
        items[IndexOf(items.ToArray(), idSelector(replacement), idSelector, kind)] = replacement;

    private static void Remove<T>(
        IList<T> items,
        string id,
        Func<T, string> idSelector,
        string kind) =>
        items.RemoveAt(IndexOf(items.ToArray(), id, idSelector, kind));

    private static OperationRejectedException Rejected(
        string code,
        string message,
        string? targetId,
        IEnumerable<string> alternatives) =>
        new(new SemanticWorkspaceError(
            code,
            message,
            null,
            targetId,
            alternatives.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Take(12).ToArray()));

    private static SemanticWorkspaceOperationResult Failure(
        SemanticWorkspaceModel current,
        SemanticWorkspaceError error) =>
        new(false, current, [], [error], null);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class WorkspaceRuntime
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Stack<HistoryEntry> Undo { get; } = new();
        public Stack<HistoryEntry> Redo { get; } = new();
        public long? HistoryRevision { get; set; }
    }

    private sealed record HistoryEntry(
        SemanticWorkspaceModel Before,
        SemanticWorkspaceModel After,
        SemanticWorkspaceTransaction Transaction);

    private sealed class MutableWorkspace
    {
        private readonly SemanticWorkspaceModel _source;

        public MutableWorkspace(SemanticWorkspaceModel source)
        {
            _source = source;
            Diagrams = source.Diagrams.ToList();
            Entities = source.Entities.ToList();
            Relationships = source.Relationships.ToList();
            Boards = source.Boards.ToList();
            Assets = source.Assets.ToList();
        }

        public List<SemanticDiagram> Diagrams { get; }
        public List<SemanticEntity> Entities { get; }
        public List<SemanticRelationship> Relationships { get; }
        public List<SemanticKanbanBoard> Boards { get; }
        public List<SemanticAssetReference> Assets { get; }
        public List<string> CreatedIds { get; } = [];
        public HashSet<string> AffectedIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> OperationByTarget { get; } = new(StringComparer.Ordinal);

        public string CreateId(string? requestedId)
        {
            var id = string.IsNullOrWhiteSpace(requestedId) ? Guid.NewGuid().ToString("N") : requestedId.Trim();
            if (AllIds().Contains(id, StringComparer.Ordinal) || CreatedIds.Contains(id, StringComparer.Ordinal))
            {
                throw Rejected("duplicate_id", $"Stable ID '{id}' already exists.", id, ["choose a different ID", "omit the create ID to generate one"]);
            }

            CreatedIds.Add(id);
            return id;
        }

        public void Affect(int operationIndex, params string?[] ids)
        {
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>())
            {
                AffectedIds.Add(id);
                OperationByTarget[id] = operationIndex;
            }
        }

        public void Add<T>(
            IList<T> items,
            T item,
            Func<T, string> idSelector,
            string kind,
            int operationIndex)
        {
            EnsureAbsent(items, idSelector(item), idSelector, kind);
            items.Add(item);
            OperationByTarget[idSelector(item)] = operationIndex;
        }

        public void Replace<T>(
            IList<T> items,
            T replacement,
            Func<T, string> idSelector,
            string kind,
            int operationIndex)
        {
            SemanticWorkspaceService.Replace(items, replacement, idSelector, kind);
            OperationByTarget[idSelector(replacement)] = operationIndex;
        }

        public void Remove<T>(
            IList<T> items,
            string id,
            Func<T, string> idSelector,
            string kind,
            int operationIndex)
        {
            SemanticWorkspaceService.Remove(items, id, idSelector, kind);
            OperationByTarget[id] = operationIndex;
        }

        public SemanticWorkspaceModel ToWorkspace(long revision, DateTimeOffset now) => _source with
        {
            Revision = revision,
            UpdatedAtUtc = now,
            Diagrams = Diagrams.ToArray(),
            Entities = Entities.ToArray(),
            Relationships = Relationships.ToArray(),
            Boards = Boards.ToArray(),
            Assets = Assets.ToArray(),
        };

        private IEnumerable<string> AllIds()
        {
            foreach (var id in new[] { _source.Id }.Concat(Diagrams.Select(item => item.Id))
                         .Concat(Entities.Select(item => item.Id))
                         .Concat(Relationships.Select(item => item.Id))
                         .Concat(Boards.Select(item => item.Id))
                         .Concat(Boards.SelectMany(item => item.Columns.Select(column => column.Id)))
                         .Concat(Assets.Select(item => item.Id))
                         .Concat(Entities.SelectMany(item => item.Blocks.Select(block => block.Id)))
                         .Concat(Entities.SelectMany(item => item.Blocks.OfType<SemanticChecklistBlock>().SelectMany(block => block.Items.Select(item => item.Id))))
                         .Concat(Entities.SelectMany(item => item.Blocks.OfType<SemanticDrawingBlock>().SelectMany(block => block.Strokes.Select(stroke => stroke.Id)))))
            {
                yield return id;
            }
        }
    }

    private sealed class OperationRejectedException(SemanticWorkspaceError error) : Exception
    {
        public SemanticWorkspaceError Error { get; } = error;
    }
}
