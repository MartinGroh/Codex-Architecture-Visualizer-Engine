using Cave.Domain;

namespace Cave.Tests;

/// <summary>
/// Verifies architecture graph invariants.
/// </summary>
public sealed class ArchitectureGraphTests
{
    /// <summary>
    /// Verifies that stable node identifiers cannot be duplicated.
    /// </summary>
    [Fact]
    public void CreateRejectsDuplicateNodeIds()
    {
        var nodes = new[] { Node("same"), Node("same") };

        var exception = Assert.Throws<ArgumentException>(() =>
            ArchitectureGraph.Create(nodes, Array.Empty<ArchitectureRelation>()));

        Assert.Contains("duplicated", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that every relation endpoint belongs to the graph.
    /// </summary>
    [Fact]
    public void CreateRejectsUnknownRelationTarget()
    {
        var relations = new[]
        {
            new ArchitectureRelation(
                "relation",
                "known",
                "missing",
                ArchitectureRelationKind.DependsOn,
                1,
                1,
                EvidenceConfidence.Exact),
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            ArchitectureGraph.Create(new[] { Node("known") }, relations));

        Assert.Contains("unknown target", exception.Message, StringComparison.Ordinal);
    }

    private static ArchitectureNode Node(string id) =>
        new(
            id,
            ArchitectureNodeKind.Class,
            id,
            null,
            id,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<SourceLocation>());
}
