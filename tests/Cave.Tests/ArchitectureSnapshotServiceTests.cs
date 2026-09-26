using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>
/// Verifies application snapshot orchestration.
/// </summary>
public sealed class ArchitectureSnapshotServiceTests
{
    /// <summary>
    /// Verifies that provider provenance remains explicit in the presentation snapshot.
    /// </summary>
    [Fact]
    public async Task GetAsyncPreservesProviderMetadata()
    {
        var expectedTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new ArchitectureSnapshotService(
            new StubSemanticIndex(),
            new GitDeltaService(new StubGitDeltaProvider(), GitBaselineRequest.Upstream),
            new EmptyActivityStore(),
            new EmptyConversationStore(),
            new FixedTimeProvider(expectedTime));

        var snapshot = await service.GetAsync("C:\\test-workspace", CancellationToken.None);

        Assert.Equal("test-provider", snapshot.Metadata.ProviderId);
        Assert.Equal(SnapshotSourceKind.CodeGraph, snapshot.Metadata.SourceKind);
        Assert.True(snapshot.Metadata.IsLive);
        Assert.Equal(expectedTime, snapshot.Metadata.GeneratedAtUtc);
        Assert.Equal(GitDeltaStatus.Ready, snapshot.Git.Status);
        Assert.Equal("origin/main", snapshot.Git.Baseline?.Reference);
        Assert.Empty(snapshot.Activity.Agents);
        Assert.False(snapshot.Conversation.SharingEnabled);
    }

    private sealed class StubGitDeltaProvider : IGitDeltaProvider
    {
        public Task<GitDeltaResult> ReadAsync(
            string workspaceRoot,
            GitBaselineRequest baseline,
            CancellationToken cancellationToken)
        {
            Assert.Equal("C:\\test-workspace", workspaceRoot);
            Assert.Equal(GitBaselineKind.Upstream, baseline.Kind);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GitDeltaResult(
                new GitBaseline(GitBaselineKind.Upstream, "origin/main", "abc123"),
                new GitWorktreeIdentity("head123", "main"),
                []));
        }
    }

    private sealed class StubSemanticIndex : ISemanticIndex
    {
        public Task<SemanticIndexResult> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken)
        {
            Assert.Equal("C:\\test-workspace", workspaceRoot);
            cancellationToken.ThrowIfCancellationRequested();
            var graph = ArchitectureGraph.Create(
                Array.Empty<ArchitectureNode>(),
                Array.Empty<ArchitectureRelation>());

            return Task.FromResult(new SemanticIndexResult(
                graph,
                "test-workspace",
                "test-provider",
                SnapshotSourceKind.CodeGraph,
                IsLive: true));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class EmptyActivityStore : IAgentActivityStore
    {
        public Task<AgentActivityOverlay> ReadAsync(
            string workspaceRoot,
            ArchitectureGraph graph,
            CancellationToken cancellationToken) =>
            Task.FromResult(AgentActivityOverlay.Empty);

        public Task AppendScopeAsync(
            string workspaceRoot,
            AgentScopeDeclaration declaration,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyConversationStore : IConversationStore
    {
        public Task<ConversationOverlay> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult(ConversationOverlay.Disabled);

        public Task SetSharingAsync(
            string workspaceRoot,
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertAsync(
            string workspaceRoot,
            ConversationMessage message,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
