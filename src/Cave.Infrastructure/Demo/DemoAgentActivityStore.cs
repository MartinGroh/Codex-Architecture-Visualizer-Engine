using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies deterministic, privacy-safe agent activity for the explicit browser demo mode.
/// </summary>
/// <param name="timeProvider">The clock used to keep demo agents visibly active.</param>
public sealed class DemoAgentActivityStore(TimeProvider timeProvider) : IAgentActivityStore, IAgentActivityHistoryStore
{
    /// <inheritdoc />
    public Task<AgentActivityOverlay> ReadAsync(
        string workspaceRoot,
        ArchitectureGraph graph,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var agents = new[]
        {
            new AgentActivity(
                "demo-main",
                "Main agent",
                IsSubagent: false,
                AgentWorkState.Active,
                AgentActivityPhase.Editing,
                "Refining semantic projection and zoom behavior",
                HasObservedActivity: true,
                HasDeclaredScope: true,
                now.AddMinutes(-7),
                now) { SummaryEvidence = AgentActivityEvidenceKind.Declared },
            new AgentActivity(
                "demo-reviewer",
                "Review agent",
                IsSubagent: true,
                AgentWorkState.Active,
                AgentActivityPhase.Validating,
                "Checking snapshot contracts and recovery states",
                HasObservedActivity: true,
                HasDeclaredScope: true,
                now.AddMinutes(-4),
                now.AddSeconds(-5)) { SummaryEvidence = AgentActivityEvidenceKind.Declared },
            new AgentActivity(
                "demo-docs",
                "Documentation agent",
                IsSubagent: true,
                AgentWorkState.Active,
                AgentActivityPhase.Reading,
                "Reviewing plugin release and onboarding guidance",
                HasObservedActivity: true,
                HasDeclaredScope: true,
                now.AddMinutes(-3),
                now.AddSeconds(-11)) { SummaryEvidence = AgentActivityEvidenceKind.Declared },
        };

        var nodes = new[]
        {
            Node("demo-main", "class:GraphProjector", AgentActivityEvidenceKind.Observed, true, now, "src/Atlas.Application/Projection/GraphProjector.cs"),
            Node("demo-main", "class:ArchitectureCanvas", AgentActivityEvidenceKind.Declared, true, now, "src/Atlas.Web/ArchitectureCanvas.tsx"),
            Node("demo-reviewer", "class:SnapshotEndpoints", AgentActivityEvidenceKind.Observed, true, now.AddSeconds(-5), "src/Atlas.Api/SnapshotEndpoints.cs"),
            Node("demo-reviewer", "class:SemanticIndexAdapter", AgentActivityEvidenceKind.Declared, true, now.AddSeconds(-5), "src/Atlas.Infrastructure/Semantics/SemanticIndexAdapter.cs"),
            Node("demo-docs", "project:Atlas.Plugin", AgentActivityEvidenceKind.Declared, true, now.AddSeconds(-11), "plugins/atlas/plugin.json"),
        };

        var recentEdits = new[]
        {
            new RecentAgentEdit("demo-main", "src/Atlas.Application/Projection/GraphProjector.cs", ["class:GraphProjector"], now.AddSeconds(-18)),
            new RecentAgentEdit("demo-main", "src/Atlas.Web/ArchitectureCanvas.tsx", ["class:ArchitectureCanvas"], now.AddSeconds(-42)),
            new RecentAgentEdit("demo-reviewer", "src/Atlas.Api/SnapshotEndpoints.cs", ["class:SnapshotEndpoints"], now.AddMinutes(-1)),
        };

        return Task.FromResult(new AgentActivityOverlay(
            agents,
            nodes,
            recentEdits,
            new AgentInstructionMarker("demo-session:turn-7", now.AddMinutes(-7)),
            [],
            Error: null)
        {
            SourceStatus = AgentActivitySourceStatus.Ready,
        });
    }

    /// <inheritdoc />
    public Task AppendScopeAsync(
        string workspaceRoot,
        AgentScopeDeclaration declaration,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The fixed CAVE demo activity stream is read-only.");

    /// <inheritdoc />
    public Task<AgentActivityHistoryReadResult> ReadHistoryAsync(
        string workspaceRoot,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (windowStartUtc > windowEndUtc)
        {
            throw new ArgumentException("The activity history window start must not follow its end.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AgentActivityHistoryReadResult.Unavailable);
    }

    private static AgentNodeActivity Node(
        string agentId,
        string nodeId,
        AgentActivityEvidenceKind evidence,
        bool isDirect,
        DateTimeOffset updatedAtUtc,
        string path) =>
        new(agentId, nodeId, evidence, isDirect, updatedAtUtc, [path]);
}
