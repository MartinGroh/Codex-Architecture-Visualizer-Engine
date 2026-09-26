namespace Cave.Domain;

/// <summary>
/// Owns a validated, internally consistent architecture graph.
/// </summary>
public sealed class ArchitectureGraph
{
    private ArchitectureGraph(
        IReadOnlyList<ArchitectureNode> nodes,
        IReadOnlyList<ArchitectureRelation> relations)
    {
        Nodes = nodes;
        Relations = relations;
    }

    /// <summary>
    /// Gets the normalized graph nodes.
    /// </summary>
    public IReadOnlyList<ArchitectureNode> Nodes { get; }

    /// <summary>
    /// Gets the normalized directed relations.
    /// </summary>
    public IReadOnlyList<ArchitectureRelation> Relations { get; }

    /// <summary>
    /// Creates a graph after validating stable identifiers and relation endpoints.
    /// </summary>
    /// <param name="nodes">The complete node set.</param>
    /// <param name="relations">The complete relation set.</param>
    /// <returns>A validated architecture graph.</returns>
    /// <exception cref="ArgumentException">A node identifier is duplicated or a relation endpoint is absent.</exception>
    public static ArchitectureGraph Create(
        IEnumerable<ArchitectureNode> nodes,
        IEnumerable<ArchitectureRelation> relations)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(relations);

        var nodeList = nodes.ToArray();
        var relationList = relations.ToArray();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodeList)
        {
            if (!nodeIds.Add(node.Id))
            {
                throw new ArgumentException($"Architecture node id '{node.Id}' is duplicated.", nameof(nodes));
            }
        }

        foreach (var relation in relationList)
        {
            if (!nodeIds.Contains(relation.SourceId))
            {
                throw new ArgumentException(
                    $"Relation '{relation.Id}' has unknown source '{relation.SourceId}'.",
                    nameof(relations));
            }

            if (!nodeIds.Contains(relation.TargetId))
            {
                throw new ArgumentException(
                    $"Relation '{relation.Id}' has unknown target '{relation.TargetId}'.",
                    nameof(relations));
            }
        }

        return new ArchitectureGraph(nodeList, relationList);
    }
}
