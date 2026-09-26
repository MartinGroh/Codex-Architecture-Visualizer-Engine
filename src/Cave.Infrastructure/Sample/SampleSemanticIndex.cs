using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Sample;

/// <summary>
/// Supplies the explicitly configured acceptance-spike graph.
/// </summary>
/// <remarks>
/// This source is labeled as non-live and is never selected as recovery from a failed provider.
/// </remarks>
public sealed class SampleSemanticIndex : ISemanticIndex
{
    private static readonly ArchitectureGraph SampleGraph = CreateSampleGraph();

    /// <inheritdoc />
    public Task<SemanticIndexResult> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SemanticIndexResult(
            SampleGraph,
            "Codex Architecture Visualizer Engine",
            "sample-semantic-index",
            SnapshotSourceKind.Sample,
            IsLive: false));
    }

    private static ArchitectureGraph CreateSampleGraph()
    {
        var nodes = new[]
        {
            Node("group:core", ArchitectureNodeKind.ArchitectureGroup, "Core intelligence", null, "Domain truth and application policy", "core"),
            Node("project:Cave.Domain", ArchitectureNodeKind.Project, "Cave.Domain", "group:core", "Stable graph concepts and invariants", "core"),
            Node("project:Cave.Application", ArchitectureNodeKind.Project, "Cave.Application", "group:core", "Projection use cases and consumer-owned ports", "core"),
            Node("group:integration", ArchitectureNodeKind.ArchitectureGroup, "Integration fabric", null, "External semantic and repository evidence", "integration"),
            Node("project:Cave.Infrastructure", ArchitectureNodeKind.Project, "Cave.Infrastructure", "group:integration", "CodeGraph, Git, filesystem, and process adapters", "integration"),
            Node("group:experience", ArchitectureNodeKind.ArchitectureGroup, "Experience layer", null, "Local host, visual surface, and Codex package", "experience"),
            Node("project:Cave.Host", ArchitectureNodeKind.Project, "Cave.Host", "group:experience", "ASP.NET Core composition, HTTP, SSE, and MCP", "experience"),
            Node("project:Cave.Ui", ArchitectureNodeKind.Project, "Cave.Ui", "group:experience", "React architecture canvas for desktop and phone", "experience"),
            Node("project:cave-plugin", ArchitectureNodeKind.Project, "CAVE plugin", "group:experience", "Codex skills, hooks, tools, and packaging", "experience"),
        };

        var relations = new[]
        {
            Relation("application-domain", "project:Cave.Application", "project:Cave.Domain", ArchitectureRelationKind.ProjectReference, 4),
            Relation("infrastructure-application", "project:Cave.Infrastructure", "project:Cave.Application", ArchitectureRelationKind.ProjectReference, 5),
            Relation("host-application", "project:Cave.Host", "project:Cave.Application", ArchitectureRelationKind.ProjectReference, 6),
            Relation("host-infrastructure", "project:Cave.Host", "project:Cave.Infrastructure", ArchitectureRelationKind.ProjectReference, 5),
            Relation("ui-host", "project:Cave.Ui", "project:Cave.Host", ArchitectureRelationKind.HttpApi, 4, EvidenceConfidence.Inferred),
            Relation("plugin-host", "project:cave-plugin", "project:Cave.Host", ArchitectureRelationKind.DependsOn, 3),
        };

        return ArchitectureGraph.Create(nodes, relations);
    }

    private static ArchitectureNode Node(
        string id,
        ArchitectureNodeKind kind,
        string name,
        string? parentId,
        string description,
        string categoryId) =>
        new(
            id,
            kind,
            name,
            parentId,
            name,
            description,
            categoryId,
            Array.Empty<string>(),
            Array.Empty<SourceLocation>());

    private static ArchitectureRelation Relation(
        string id,
        string sourceId,
        string targetId,
        ArchitectureRelationKind kind,
        int evidenceCount,
        EvidenceConfidence confidence = EvidenceConfidence.Exact) =>
        new(id, sourceId, targetId, kind, evidenceCount, evidenceCount, confidence);
}
