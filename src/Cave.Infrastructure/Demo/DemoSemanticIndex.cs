using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies the expanded, sanitized architecture graph for explicit demo mode.
/// </summary>
/// <remarks>
/// This source is labeled as non-live and is never selected as recovery from a failed provider.
/// </remarks>
public sealed class DemoSemanticIndex : ISemanticIndex
{
    private static readonly ArchitectureGraph DemoGraph = CreateDemoGraph();

    /// <inheritdoc />
    public Task<SemanticIndexResult> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SemanticIndexResult(
            DemoGraph,
            "Atlas Workspace — CAVE Demo",
            "cave-demo-1",
            SnapshotSourceKind.Sample,
            IsLive: false));
    }

    private static ArchitectureGraph CreateDemoGraph()
    {
        var nodes = new[]
        {
            Node("group:core", ArchitectureNodeKind.ArchitectureGroup, "Core intelligence", null, "Domain truth and projection policy", "core"),
            Node("project:Atlas.Domain", ArchitectureNodeKind.Project, "Atlas.Domain", "group:core", "Stable architecture concepts and invariants", "core", "src/Atlas.Domain"),
            Node("namespace:Atlas.Domain.Model", ArchitectureNodeKind.Namespace, "Atlas.Domain.Model", "project:Atlas.Domain", "Normalized architecture evidence", "core", "Atlas.Domain.Model"),
            Node("class:ArchitectureGraph", ArchitectureNodeKind.Class, "ArchitectureGraph", "namespace:Atlas.Domain.Model", "Immutable nodes and dependency relations", "core", "Atlas.Domain.Model.ArchitectureGraph", "src/Atlas.Domain/Model/ArchitectureGraph.cs", 8, 148),
            Node("interface:EvidenceSource", ArchitectureNodeKind.Interface, "IEvidenceSource", "namespace:Atlas.Domain.Model", "Identifies provenance without conflating evidence", "core", "Atlas.Domain.Model.IEvidenceSource", "src/Atlas.Domain/Model/IEvidenceSource.cs", 6, 44),
            Node("project:Atlas.Application", ArchitectureNodeKind.Project, "Atlas.Application", "group:core", "Use cases and consumer-owned ports", "core", "src/Atlas.Application"),
            Node("namespace:Atlas.Application.Projection", ArchitectureNodeKind.Namespace, "Atlas.Application.Projection", "project:Atlas.Application", "Semantic projection and impact analysis", "core", "Atlas.Application.Projection"),
            Node("class:GraphProjector", ArchitectureNodeKind.Class, "GraphProjector", "namespace:Atlas.Application.Projection", "Projects one canonical graph at each semantic level", "core", "Atlas.Application.Projection.GraphProjector", "src/Atlas.Application/Projection/GraphProjector.cs", 12, 220),
            Node("interface:ProjectionPolicy", ArchitectureNodeKind.Interface, "IProjectionPolicy", "namespace:Atlas.Application.Projection", "Owns explicit semantic-level selection", "core", "Atlas.Application.Projection.IProjectionPolicy", "src/Atlas.Application/Projection/IProjectionPolicy.cs", 5, 54),

            Node("group:integration", ArchitectureNodeKind.ArchitectureGroup, "Integration fabric", null, "External code and repository evidence", "integration"),
            Node("project:Atlas.Infrastructure", ArchitectureNodeKind.Project, "Atlas.Infrastructure", "group:integration", "Adapters for semantic and Git providers", "integration", "src/Atlas.Infrastructure"),
            Node("namespace:Atlas.Infrastructure.Semantics", ArchitectureNodeKind.Namespace, "Atlas.Infrastructure.Semantics", "project:Atlas.Infrastructure", "External semantic-provider translation", "integration", "Atlas.Infrastructure.Semantics"),
            Node("class:SemanticIndexAdapter", ArchitectureNodeKind.Class, "SemanticIndexAdapter", "namespace:Atlas.Infrastructure.Semantics", "Maps provider records into the domain graph", "integration", "Atlas.Infrastructure.Semantics.SemanticIndexAdapter", "src/Atlas.Infrastructure/Semantics/SemanticIndexAdapter.cs", 10, 188),
            Node("namespace:Atlas.Infrastructure.VersionControl", ArchitectureNodeKind.Namespace, "Atlas.Infrastructure.VersionControl", "project:Atlas.Infrastructure", "Version-control evidence acquisition", "integration", "Atlas.Infrastructure.VersionControl"),
            Node("class:GitDeltaProvider", ArchitectureNodeKind.Class, "GitDeltaProvider", "namespace:Atlas.Infrastructure.VersionControl", "Reads exact baselines, hunks, and worktree identity", "integration", "Atlas.Infrastructure.VersionControl.GitDeltaProvider", "src/Atlas.Infrastructure/VersionControl/GitDeltaProvider.cs", 9, 176),

            Node("group:experience", ArchitectureNodeKind.ArchitectureGroup, "Product experience", null, "API, visual canvas, and agent integration", "experience"),
            Node("project:Atlas.Api", ArchitectureNodeKind.Project, "Atlas.Api", "group:experience", "Read-only snapshot and event endpoints", "experience", "src/Atlas.Api"),
            Node("class:SnapshotEndpoints", ArchitectureNodeKind.Class, "SnapshotEndpoints", "project:Atlas.Api", "HTTP and SSE presentation boundary", "experience", "Atlas.Api.SnapshotEndpoints", "src/Atlas.Api/SnapshotEndpoints.cs", 7, 132),
            Node("project:Atlas.Web", ArchitectureNodeKind.Project, "Atlas.Web", "group:experience", "Interactive React architecture canvas", "experience", "src/Atlas.Web"),
            Node("class:ArchitectureCanvas", ArchitectureNodeKind.Class, "ArchitectureCanvas", "project:Atlas.Web", "Semantic zoom, focus, and graph interaction", "experience", "Atlas.Web.ArchitectureCanvas", "src/Atlas.Web/ArchitectureCanvas.tsx", 18, 246),
            Node("class:AgentSpotlight", ArchitectureNodeKind.Class, "AgentSpotlight", "project:Atlas.Web", "Phase-aware live agent presentation", "experience", "Atlas.Web.AgentSpotlight", "src/Atlas.Web/AgentSpotlight.tsx", 9, 118),
            Node("project:Atlas.Plugin", ArchitectureNodeKind.Project, "Atlas.Plugin", "group:experience", "Codex skills, hooks, tools, and packaging", "experience", "plugins/atlas"),
        };

        var relations = new[]
        {
            Relation("application-domain", "project:Atlas.Application", "project:Atlas.Domain", ArchitectureRelationKind.ProjectReference, 8),
            Relation("infrastructure-application", "project:Atlas.Infrastructure", "project:Atlas.Application", ArchitectureRelationKind.ProjectReference, 7),
            Relation("api-application", "project:Atlas.Api", "project:Atlas.Application", ArchitectureRelationKind.ProjectReference, 9),
            Relation("api-infrastructure", "project:Atlas.Api", "project:Atlas.Infrastructure", ArchitectureRelationKind.ProjectReference, 5),
            Relation("web-api", "project:Atlas.Web", "project:Atlas.Api", ArchitectureRelationKind.HttpApi, 6, EvidenceConfidence.Inferred),
            Relation("plugin-api", "project:Atlas.Plugin", "project:Atlas.Api", ArchitectureRelationKind.DependsOn, 4),
            Relation("projector-graph", "class:GraphProjector", "class:ArchitectureGraph", ArchitectureRelationKind.DependsOn, 12),
            Relation("projector-policy", "class:GraphProjector", "interface:ProjectionPolicy", ArchitectureRelationKind.DependsOn, 7),
            Relation("semantic-source", "class:SemanticIndexAdapter", "interface:EvidenceSource", ArchitectureRelationKind.Implements, 1),
            Relation("semantic-graph", "class:SemanticIndexAdapter", "class:ArchitectureGraph", ArchitectureRelationKind.DependsOn, 8),
            Relation("git-source", "class:GitDeltaProvider", "interface:EvidenceSource", ArchitectureRelationKind.Implements, 1),
            Relation("endpoint-projector", "class:SnapshotEndpoints", "class:GraphProjector", ArchitectureRelationKind.DependsOn, 6),
            Relation("canvas-endpoints", "class:ArchitectureCanvas", "class:SnapshotEndpoints", ArchitectureRelationKind.HttpApi, 5, EvidenceConfidence.Inferred),
            Relation("spotlight-canvas", "class:AgentSpotlight", "class:ArchitectureCanvas", ArchitectureRelationKind.DependsOn, 3),
        };

        return ArchitectureGraph.Create(nodes, relations);
    }

    private static ArchitectureNode Node(
        string id,
        ArchitectureNodeKind kind,
        string name,
        string? parentId,
        string description,
        string categoryId,
        string? qualifiedName = null,
        string? filePath = null,
        int startLine = 1,
        int endLine = 1) =>
        new(
            id,
            kind,
            name,
            parentId,
            qualifiedName ?? name,
            description,
            categoryId,
            [kind.ToString().ToLowerInvariant(), "demo"],
            filePath is null ? [] : [new SourceLocation(filePath, startLine, endLine)]);

    private static ArchitectureRelation Relation(
        string id,
        string sourceId,
        string targetId,
        ArchitectureRelationKind kind,
        int evidenceCount,
        EvidenceConfidence confidence = EvidenceConfidence.Exact) =>
        new(id, sourceId, targetId, kind, evidenceCount, evidenceCount, confidence);
}
