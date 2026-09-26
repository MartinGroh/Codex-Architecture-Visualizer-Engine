using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies deterministic, sanitized Git evidence for the explicit browser demo mode.
/// </summary>
/// <remarks>
/// This adapter is selected only by the host composition root when <c>Cave:DemoMode</c> is true.
/// It is never used to recover from a native Git failure.
/// </remarks>
public sealed class DemoGitDeltaProvider : IGitDeltaProvider
{
    /// <inheritdoc />
    public Task<GitDeltaResult> ReadAsync(
        string workspaceRoot,
        GitBaselineRequest baseline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var files = new[]
        {
            File("src/Atlas.Application/Projection/GraphProjector.cs", GitFileChangeKind.Modified, 78, 21, 72, 78, 68, 21),
            File("src/Atlas.Infrastructure/Semantics/SemanticIndexAdapter.cs", GitFileChangeKind.Modified, 52, 12, 64, 52, 60, 12),
            File("src/Atlas.Api/SnapshotEndpoints.cs", GitFileChangeKind.Modified, 28, 8, 42, 28, 40, 8),
            File("src/Atlas.Web/ArchitectureCanvas.tsx", GitFileChangeKind.Modified, 114, 34, 86, 114, 80, 34),
            File("src/Atlas.Web/AgentSpotlight.tsx", GitFileChangeKind.Added, 118, 0, 1, 118, 0, 0),
            File("docs/architecture/semantic-projection.md", GitFileChangeKind.Added, 64, 0, 1, 64, 0, 0),
        };

        return Task.FromResult(new GitDeltaResult(
            new GitBaseline(
                baseline.Kind,
                baseline.Kind == GitBaselineKind.Upstream ? "origin/main" : "demo-baseline",
                "8e3b4e31cc73a2d1d98c1c0d9319bafe4ce74020"),
            new GitWorktreeIdentity(
                "c41a6c29f4dd7cbe59c0839fd8f0a0e62db93d84",
                "feature/semantic-focus"),
            files));
    }

    private static GitFileDelta File(
        string path,
        GitFileChangeKind kind,
        int additions,
        int deletions,
        int newStart,
        int newCount,
        int oldStart,
        int oldCount) =>
        new(
            path,
            null,
            kind,
            additions,
            deletions,
            IsBinary: false,
            [new GitHunkDelta(oldStart, oldCount, newStart, newCount)]);
}
