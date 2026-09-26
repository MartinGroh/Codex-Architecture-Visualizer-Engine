using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Produces deterministic semantic summaries, complete diagram reads, and bounded entity context from an already-loaded workspace.
/// </summary>
public static class SemanticWorkspaceReadModelService
{
    /// <summary>Gets the default maximum number of entities returned by one context request.</summary>
    public const int DefaultMaximumEntities = 100;

    /// <summary>Gets the largest supported entity-context traversal depth.</summary>
    public const int MaximumDepth = 8;

    /// <summary>Gets the largest supported entity count in one context response.</summary>
    public const int MaximumEntities = 500;

    /// <summary>Summarizes the canonical semantic workspace without presentation state.</summary>
    /// <param name="workspace">The already-loaded canonical workspace.</param>
    /// <returns>A deterministic workspace summary.</returns>
    public static SemanticWorkspaceSummary GetWorkspaceSummary(Cave.Domain.SemanticWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var summaries = workspace.Diagrams
            .OrderBy(diagram => diagram.Id, StringComparer.Ordinal)
            .Select(diagram => CreateDiagramSummary(workspace, diagram))
            .ToArray();

        return new SemanticWorkspaceSummary(
            workspace.SchemaVersion,
            workspace.Id,
            workspace.Name,
            workspace.Revision,
            workspace.UpdatedAtUtc,
            workspace.Diagrams.Count,
            workspace.Entities.Count,
            workspace.Relationships.Count,
            workspace.Boards.Count,
            workspace.Assets.Count,
            workspace.Diagrams.Count(diagram => diagram.Tracking is not null),
            workspace.Entities.Count(entity => entity.Tracking is not null),
            summaries);
    }

    /// <summary>Finds a deterministic summary for one semantic diagram.</summary>
    /// <param name="workspace">The already-loaded canonical workspace.</param>
    /// <param name="diagramId">The stable diagram identifier.</param>
    /// <returns>The summary, or <see langword="null"/> when the identifier is unknown.</returns>
    public static SemanticDiagramSummary? FindDiagramSummary(
        Cave.Domain.SemanticWorkspace workspace,
        string diagramId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagramId);
        var diagram = workspace.Diagrams.FirstOrDefault(item =>
            string.Equals(item.Id, diagramId, StringComparison.Ordinal));
        return diagram is null ? null : CreateDiagramSummary(workspace, diagram);
    }

    /// <summary>Finds the complete canonical read model for one semantic diagram.</summary>
    /// <param name="workspace">The already-loaded canonical workspace.</param>
    /// <param name="diagramId">The stable diagram identifier.</param>
    /// <returns>The diagram read model, or <see langword="null"/> when the identifier is unknown.</returns>
    public static SemanticDiagramReadModel? FindDiagram(
        Cave.Domain.SemanticWorkspace workspace,
        string diagramId)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagramId);
        var diagram = workspace.Diagrams.FirstOrDefault(item =>
            string.Equals(item.Id, diagramId, StringComparison.Ordinal));
        if (diagram is null)
        {
            return null;
        }

        var entities = workspace.Entities
            .Where(entity => string.Equals(entity.DiagramId, diagram.Id, StringComparison.Ordinal))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();
        var relationships = workspace.Relationships
            .Where(relationship => string.Equals(relationship.DiagramId, diagram.Id, StringComparison.Ordinal))
            .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();

        var childDiagramIds = workspace.Diagrams
            .Where(item => string.Equals(item.ParentDiagramId, diagram.Id, StringComparison.Ordinal))
            .Select(item => item.Id)
            .Concat(entities.Select(entity => entity.ChildDiagramId).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        var childDiagrams = workspace.Diagrams
            .Where(item => childDiagramIds.Contains(item.Id))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => CreateDiagramSummary(workspace, item))
            .ToArray();

        var assetIds = entities
            .SelectMany(entity => entity.Blocks)
            .OfType<SemanticImageBlock>()
            .Select(block => block.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        var assets = workspace.Assets
            .Where(asset => assetIds.Contains(asset.Id))
            .OrderBy(asset => asset.Id, StringComparer.Ordinal)
            .ToArray();

        var boardIds = entities
            .Select(entity => entity.Tracking?.BoardId)
            .Append(diagram.Tracking?.BoardId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var boards = workspace.Boards
            .Where(board => boardIds.Contains(board.Id))
            .OrderBy(board => board.Id, StringComparer.Ordinal)
            .ToArray();

        return new SemanticDiagramReadModel(
            diagram,
            entities,
            relationships,
            childDiagrams,
            assets,
            boards);
    }

    /// <summary>Finds bounded context for a semantic entity.</summary>
    /// <param name="workspace">The already-loaded canonical workspace.</param>
    /// <param name="entityId">The stable semantic entity identifier.</param>
    /// <param name="depth">The relationship and hierarchy traversal depth, from zero through eight.</param>
    /// <param name="maximumEntities">The response entity bound, from one through five hundred.</param>
    /// <returns>The bounded context, or <see langword="null"/> when the entity is unknown.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The requested bounds are outside supported limits.</exception>
    public static SemanticEntityContext? FindEntityContext(
        Cave.Domain.SemanticWorkspace workspace,
        string entityId,
        int depth = 1,
        int maximumEntities = DefaultMaximumEntities)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ValidateContextBounds(depth, maximumEntities);

        var entitiesById = workspace.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        if (!entitiesById.ContainsKey(entityId))
        {
            return null;
        }

        var entitiesByDiagram = workspace.Entities
            .GroupBy(entity => entity.DiagramId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var childrenByParent = workspace.Entities
            .Where(entity => entity.ParentEntityId is not null)
            .GroupBy(entity => entity.ParentEntityId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var relationshipsByEntity = BuildRelationshipIndex(workspace.Relationships);
        var ownerByChildDiagram = workspace.Entities
            .Where(entity => entity.ChildDiagramId is not null)
            .GroupBy(entity => entity.ChildDiagramId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entity => entity.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var queue = new Queue<string>();
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [entityId] = 0 };
        var connections = new Dictionary<string, HashSet<SemanticContextConnectionKind>>(StringComparer.Ordinal)
        {
            [entityId] = [SemanticContextConnectionKind.Root],
        };
        var discoveryOrder = new List<string> { entityId };
        var truncated = false;
        queue.Enqueue(entityId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var currentDepth = distances[currentId];
            if (currentDepth >= depth)
            {
                continue;
            }

            var neighbors = GetNeighbors(
                    entitiesById[currentId],
                    entitiesById,
                    entitiesByDiagram,
                    childrenByParent,
                    relationshipsByEntity,
                    ownerByChildDiagram)
                .OrderBy(neighbor => neighbor.EntityId, StringComparer.Ordinal)
                .ThenBy(neighbor => neighbor.Kind)
                .ToArray();

            foreach (var neighbor in neighbors)
            {
                if (distances.TryGetValue(neighbor.EntityId, out var knownDepth))
                {
                    if (knownDepth == currentDepth + 1)
                    {
                        connections[neighbor.EntityId].Add(neighbor.Kind);
                    }

                    continue;
                }

                if (discoveryOrder.Count >= maximumEntities)
                {
                    truncated = true;
                    continue;
                }

                distances[neighbor.EntityId] = currentDepth + 1;
                connections[neighbor.EntityId] = [neighbor.Kind];
                discoveryOrder.Add(neighbor.EntityId);
                queue.Enqueue(neighbor.EntityId);
            }
        }

        var contextEntities = discoveryOrder
            .Select(id => new SemanticContextEntity(
                entitiesById[id],
                distances[id],
                connections[id].OrderBy(kind => kind).ToArray()))
            .ToArray();
        var includedIds = discoveryOrder.ToHashSet(StringComparer.Ordinal);
        var relationships = workspace.Relationships
            .Where(relationship =>
                includedIds.Contains(relationship.SourceEntityId) &&
                includedIds.Contains(relationship.TargetEntityId))
            .OrderBy(relationship => relationship.Id, StringComparer.Ordinal)
            .ToArray();
        var includedDiagramIds = contextEntities
            .Select(item => item.Entity.DiagramId)
            .ToHashSet(StringComparer.Ordinal);
        var diagrams = workspace.Diagrams
            .Where(diagram => includedDiagramIds.Contains(diagram.Id))
            .OrderBy(diagram => diagram.Id, StringComparer.Ordinal)
            .Select(diagram => CreateDiagramSummary(workspace, diagram))
            .ToArray();
        var assetIds = contextEntities
            .SelectMany(item => item.Entity.Blocks)
            .OfType<SemanticImageBlock>()
            .Select(block => block.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        var assets = workspace.Assets
            .Where(asset => assetIds.Contains(asset.Id))
            .OrderBy(asset => asset.Id, StringComparer.Ordinal)
            .ToArray();

        return new SemanticEntityContext(
            entityId,
            depth,
            maximumEntities,
            truncated,
            contextEntities,
            relationships,
            diagrams,
            assets);
    }

    /// <summary>Finds bounded contexts for every semantic entity bound to one architecture node.</summary>
    /// <param name="workspace">The already-loaded canonical workspace.</param>
    /// <param name="architectureNodeId">The stable CodeGraph architecture-node identifier.</param>
    /// <param name="depth">The relationship and hierarchy traversal depth.</param>
    /// <param name="maximumEntities">The response entity bound for each matched context.</param>
    /// <returns>Zero or more deterministic contexts for semantic entities bound to the architecture node.</returns>
    public static IReadOnlyList<SemanticEntityContext> FindArchitectureNodeContexts(
        Cave.Domain.SemanticWorkspace workspace,
        string architectureNodeId,
        int depth = 1,
        int maximumEntities = DefaultMaximumEntities)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(architectureNodeId);
        ValidateContextBounds(depth, maximumEntities);

        return workspace.Entities
            .Where(entity => string.Equals(
                entity.ArchitectureNodeId,
                architectureNodeId,
                StringComparison.Ordinal))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .Select(entity => FindEntityContext(workspace, entity.Id, depth, maximumEntities)!)
            .ToArray();
    }

    private static SemanticDiagramSummary CreateDiagramSummary(
        Cave.Domain.SemanticWorkspace workspace,
        SemanticDiagram diagram)
    {
        var entities = workspace.Entities
            .Where(entity => string.Equals(entity.DiagramId, diagram.Id, StringComparison.Ordinal))
            .ToArray();
        var explicitChildren = workspace.Diagrams
            .Where(candidate => string.Equals(candidate.ParentDiagramId, diagram.Id, StringComparison.Ordinal))
            .Select(candidate => candidate.Id);
        var linkedChildren = entities.Select(entity => entity.ChildDiagramId).OfType<string>();

        return new SemanticDiagramSummary(
            diagram.Id,
            diagram.Title,
            diagram.Purpose,
            diagram.ParentDiagramId,
            entities.Length,
            workspace.Relationships.Count(relationship =>
                string.Equals(relationship.DiagramId, diagram.Id, StringComparison.Ordinal)),
            explicitChildren.Concat(linkedChildren).Distinct(StringComparer.Ordinal).Count(),
            entities.Sum(entity => entity.Blocks.Count),
            entities.Count(entity => entity.Tracking is not null),
            diagram.Tracking,
            SortMetadata(diagram.Metadata));
    }

    private static Dictionary<string, IReadOnlyList<SemanticRelationship>> BuildRelationshipIndex(
        IReadOnlyList<SemanticRelationship> relationships)
    {
        var index = new Dictionary<string, List<SemanticRelationship>>(StringComparer.Ordinal);
        foreach (var relationship in relationships.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            Add(relationship.SourceEntityId, relationship);
            Add(relationship.TargetEntityId, relationship);
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<SemanticRelationship>)pair.Value,
            StringComparer.Ordinal);

        void Add(string entityId, SemanticRelationship relationship)
        {
            if (!index.TryGetValue(entityId, out var items))
            {
                items = [];
                index.Add(entityId, items);
            }

            items.Add(relationship);
        }
    }

    private static IEnumerable<ContextNeighbor> GetNeighbors(
        SemanticEntity current,
        Dictionary<string, SemanticEntity> entitiesById,
        Dictionary<string, SemanticEntity[]> entitiesByDiagram,
        Dictionary<string, SemanticEntity[]> childrenByParent,
        Dictionary<string, IReadOnlyList<SemanticRelationship>> relationshipsByEntity,
        Dictionary<string, SemanticEntity[]> ownerByChildDiagram)
    {
        if (current.ParentEntityId is not null && entitiesById.ContainsKey(current.ParentEntityId))
        {
            yield return new ContextNeighbor(current.ParentEntityId, SemanticContextConnectionKind.Parent);
        }

        if (childrenByParent.TryGetValue(current.Id, out var children))
        {
            foreach (var child in children)
            {
                yield return new ContextNeighbor(child.Id, SemanticContextConnectionKind.Child);
            }
        }

        if (relationshipsByEntity.TryGetValue(current.Id, out var relationships))
        {
            foreach (var relationship in relationships)
            {
                var neighborId = string.Equals(relationship.SourceEntityId, current.Id, StringComparison.Ordinal)
                    ? relationship.TargetEntityId
                    : relationship.SourceEntityId;
                if (entitiesById.ContainsKey(neighborId))
                {
                    yield return new ContextNeighbor(neighborId, SemanticContextConnectionKind.Relationship);
                }
            }
        }

        if (current.ChildDiagramId is not null &&
            entitiesByDiagram.TryGetValue(current.ChildDiagramId, out var childDiagramEntities))
        {
            foreach (var child in childDiagramEntities.Where(entity => entity.ParentEntityId is null))
            {
                yield return new ContextNeighbor(child.Id, SemanticContextConnectionKind.ChildDiagram);
            }
        }

        if (ownerByChildDiagram.TryGetValue(current.DiagramId, out var diagramOwners))
        {
            foreach (var owner in diagramOwners)
            {
                yield return new ContextNeighbor(owner.Id, SemanticContextConnectionKind.ParentDiagram);
            }
        }
    }

    private static void ValidateContextBounds(int depth, int maximumEntities)
    {
        if (depth is < 0 or > MaximumDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                depth,
                $"Semantic context depth must be between 0 and {MaximumDepth}.");
        }

        if (maximumEntities is < 1 or > MaximumEntities)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEntities),
                maximumEntities,
                $"Semantic context entity bound must be between 1 and {MaximumEntities}.");
        }
    }

    private static SortedDictionary<string, string> SortMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            sorted.Add(pair.Key, pair.Value);
        }

        return sorted;
    }

    private sealed record ContextNeighbor(string EntityId, SemanticContextConnectionKind Kind);
}
