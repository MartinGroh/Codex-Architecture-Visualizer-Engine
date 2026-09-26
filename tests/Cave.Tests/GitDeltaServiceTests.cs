using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>
/// Verifies deterministic projection of Git hunks onto the normalized architecture hierarchy.
/// </summary>
public sealed class GitDeltaServiceTests
{
    private static readonly string[] AncestorNodeIds =
        ["class:widget", "namespace:app", "project:app", "group:workspace"];

    /// <summary>
    /// Verifies that the most specific source owner receives a hunk and every ancestor receives its aggregate.
    /// </summary>
    [Fact]
    public async Task GetAsyncProjectsHunksAndAggregatesAncestors()
    {
        var changedFile = new GitFileDelta(
            "src/App/Widget.cs",
            null,
            GitFileChangeKind.Modified,
            3,
            1,
            IsBinary: false,
            [new GitHunkDelta(12, 1, 12, 3)]);
        var unmappedFile = new GitFileDelta(
            "README.md",
            null,
            GitFileChangeKind.Untracked,
            2,
            0,
            IsBinary: false,
            [new GitHunkDelta(0, 0, 1, 2)]);
        var provider = new StubGitDeltaProvider([changedFile, unmappedFile]);
        var service = new GitDeltaService(provider, GitBaselineRequest.Upstream);
        var graph = CreateGraph();

        var overlay = await service.GetAsync("C:\\workspace", graph, CancellationToken.None);

        Assert.Equal(GitDeltaStatus.Ready, overlay.Status);
        Assert.Equal("origin/main", overlay.Baseline?.Reference);
        Assert.Equal("main", overlay.Worktree?.Branch);
        Assert.Equal("head123", overlay.Worktree?.HeadSha);
        Assert.Equal("class:widget", Assert.Single(overlay.Nodes, item => item.NodeId == "class:widget").NodeId);
        Assert.All(
            AncestorNodeIds,
            nodeId =>
            {
                var delta = Assert.Single(overlay.Nodes, item => item.NodeId == nodeId);
                Assert.True(delta.Additions >= 3);
                Assert.True(delta.Deletions >= 1);
            });
        Assert.Equal("README.md", Assert.Single(overlay.UnmappedFiles).FilePath);
    }

    /// <summary>
    /// Verifies that an expected Git boundary failure remains explicit instead of choosing another baseline.
    /// </summary>
    [Fact]
    public async Task GetAsyncReportsUnavailableWithoutFallback()
    {
        var service = new GitDeltaService(new UnavailableGitDeltaProvider(), GitBaselineRequest.Upstream);

        var overlay = await service.GetAsync(
            "C:\\workspace",
            ArchitectureGraph.Create([], []),
            CancellationToken.None);

        Assert.Equal(GitDeltaStatus.Unavailable, overlay.Status);
        Assert.Null(overlay.Baseline);
        Assert.Contains("upstream", overlay.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ArchitectureGraph CreateGraph() => ArchitectureGraph.Create(
        [
            Node("group:workspace", ArchitectureNodeKind.ArchitectureGroup, "Workspace", null, null, []),
            Node("project:app", ArchitectureNodeKind.Project, "App", "group:workspace", "src/App", []),
            Node("namespace:app", ArchitectureNodeKind.Namespace, "App", "project:app", "App", []),
            Node(
                "class:widget",
                ArchitectureNodeKind.Class,
                "Widget",
                "namespace:app",
                "App.Widget",
                [new SourceLocation("src/App/Widget.cs", 10, 30)]),
        ],
        []);

    private static ArchitectureNode Node(
        string id,
        ArchitectureNodeKind kind,
        string name,
        string? parentId,
        string? qualifiedName,
        IReadOnlyList<SourceLocation> sourceLocations) => new(
            id,
            kind,
            name,
            parentId,
            qualifiedName,
            null,
            null,
            [],
            sourceLocations);

    private sealed class StubGitDeltaProvider(IReadOnlyList<GitFileDelta> files) : IGitDeltaProvider
    {
        public Task<GitDeltaResult> ReadAsync(
            string workspaceRoot,
            GitBaselineRequest baseline,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GitDeltaResult(
                new GitBaseline(baseline.Kind, "origin/main", "abc123"),
                new GitWorktreeIdentity("head123", "main"),
                files));
        }
    }

    private sealed class UnavailableGitDeltaProvider : IGitDeltaProvider
    {
        public Task<GitDeltaResult> ReadAsync(
            string workspaceRoot,
            GitBaselineRequest baseline,
            CancellationToken cancellationToken) =>
            throw new GitDeltaUnavailableException("The tracked upstream is unavailable.");
    }
}
