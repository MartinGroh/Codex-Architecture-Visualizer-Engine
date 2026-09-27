using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Demo;

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
            new WorkspaceConversationService(new EmptyConversationStore(), new RecordingGoalProvider(),
                new FixedTimeProvider(expectedTime)),
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
        Assert.Null(snapshot.Conversation.Goal);
    }

    /// <summary>Disabled sharing and missing exact-task bindings never trigger goal reads.</summary>
    [Theory]
    [InlineData(false, "task-a")]
    [InlineData(true, null)]
    public async Task GoalReadRequiresSharingAndExactTask(bool sharing, string? sessionId)
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(sharing, sessionId) };
        var goals = new RecordingGoalProvider();
        var service = CreateService(conversations, goals);

        var snapshot = await service.GetAsync("C:\\test-workspace", CancellationToken.None);

        Assert.Null(snapshot.Conversation.Goal);
        Assert.Empty(goals.Sessions);
    }

    /// <summary>Each refresh reads only the currently bound task and clears previously shared goal data.</summary>
    [Fact]
    public async Task GoalRefreshTracksTaskSwitchAndDisable()
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(true, "task-a") };
        var goals = new RecordingGoalProvider();
        var service = CreateService(conversations, goals);
        var first = await service.GetAsync("C:\\test-workspace", CancellationToken.None);
        Assert.Equal("task-a", first.Conversation.Goal?.SessionId);
        Assert.Equal("Goal for task-a", first.Conversation.Goal?.Goal?.Objective);

        conversations.Overlay = Conversation(true, "task-b");
        var switched = await service.RefreshLiveOverlaysAsync("C:\\test-workspace", first, CancellationToken.None);
        Assert.Equal("task-b", switched.Conversation.Goal?.SessionId);
        Assert.Equal("Goal for task-b", switched.Conversation.Goal?.Goal?.Objective);

        conversations.Overlay = Conversation(false, "task-b") with { Goal = switched.Conversation.Goal };
        var disabled = await service.RefreshLiveOverlaysAsync("C:\\test-workspace", switched, CancellationToken.None);
        Assert.Null(disabled.Conversation.Goal);
        Assert.Collection(goals.Sessions,
            session => Assert.Equal("task-a", session), session => Assert.Equal("task-b", session));
        Assert.Same(first.Graph, disabled.Graph);
        Assert.Same(first.Git, disabled.Git);
    }

    /// <summary>A privacy or task change during an external read discards its objective before publication.</summary>
    [Theory]
    [InlineData(false, "task-a")]
    [InlineData(true, "task-b")]
    public async Task InFlightGoalReadRechecksSharingAndTask(bool sharing, string newSessionId)
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(true, "task-a") };
        var goals = new RecordingGoalProvider
        {
            OnRead = () => conversations.Overlay = Conversation(sharing, newSessionId),
        };

        var snapshot = await CreateService(conversations, goals).GetAsync("C:\\test-workspace", CancellationToken.None);

        Assert.Null(snapshot.Conversation.Goal);
        Assert.Equal(newSessionId, snapshot.Conversation.Control.SessionId);
        Assert.Equal(sharing, snapshot.Conversation.SharingEnabled);
        Assert.Equal("task-a", Assert.Single(goals.Sessions));
    }

    /// <summary>Foreign-task results and unavailable results expose no objective, while no-goal is explicit.</summary>
    [Theory]
    [InlineData("task-b", CodexGoalSourceStatus.Ready, true)]
    [InlineData("task-a", CodexGoalSourceStatus.Unavailable, false)]
    [InlineData("task-a", CodexGoalSourceStatus.Ready, false)]
    public async Task GoalResultPreservesAbsenceAndRejectsForeignTask(
        string returnedSessionId, CodexGoalSourceStatus status, bool hasGoal)
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(true, "task-a") };
        var goals = new RecordingGoalProvider
        {
            Result = new CodexGoalSnapshot(status, returnedSessionId,
                hasGoal ? NativeGoal("Private foreign goal") : null, DateTimeOffset.UnixEpoch,
                status == CodexGoalSourceStatus.Unavailable ? "Endpoint unavailable" : null),
        };

        var snapshot = await CreateService(conversations, goals).GetAsync("C:\\test-workspace", CancellationToken.None);

        var goal = Assert.IsType<CodexGoalSnapshot>(snapshot.Conversation.Goal);
        Assert.Equal("task-a", goal.SessionId);
        Assert.Null(goal.Goal);
        Assert.Equal(returnedSessionId == "task-a" ? status : CodexGoalSourceStatus.Unavailable, goal.Status);
    }

    /// <summary>The explicit demo has a synthetic main goal and cannot send work to a real task.</summary>
    [Fact]
    public async Task DemoEnrichmentUsesOnlySyntheticTask()
    {
        var snapshot = await CreateService(new DemoConversationStore(TimeProvider.System),
            new DemoCodexGoalProvider(TimeProvider.System)).GetAsync("C:\\test-workspace", CancellationToken.None);

        Assert.Equal("demo-session", snapshot.Conversation.Control.SessionId);
        Assert.False(snapshot.Conversation.Control.CanSend);
        var result = Assert.IsType<CodexGoalSnapshot>(snapshot.Conversation.Goal);
        Assert.Equal(CodexGoalSourceStatus.Ready, result.Status);
        Assert.Equal("demo-session", result.SessionId);
        Assert.NotNull(result.Goal);
        Assert.Equal(CodexGoalStatus.Active, result.Goal.Status);
        Assert.Equal("Make architecture changes and agent work easy to follow", result.Goal.Objective);
    }

    private static ArchitectureSnapshotService CreateService(IConversationStore conversations, ICodexGoalProvider goals) =>
        new(new StubSemanticIndex(), new GitDeltaService(new StubGitDeltaProvider(), GitBaselineRequest.Upstream),
            new EmptyActivityStore(), new WorkspaceConversationService(conversations, goals, TimeProvider.System),
            TimeProvider.System);

    private static ConversationOverlay Conversation(bool enabled, string? sessionId) =>
        new(enabled, enabled ? ConversationSourceStatus.Ready : ConversationSourceStatus.Disabled, [],
            ConversationControl.Unavailable with { SessionId = sessionId }, null);

    private static CodexGoal NativeGoal(string objective) =>
        new(objective, CodexGoalStatus.Active, null, 0, 0, 1_000, 1_001);

    private sealed class RecordingGoalProvider : ICodexGoalProvider
    {
        internal List<string> Sessions { get; } = [];
        internal Action? OnRead { get; init; }
        internal CodexGoalSnapshot? Result { get; init; }

        public Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sessions.Add(sessionId);
            OnRead?.Invoke();
            return Task.FromResult(Result ?? new CodexGoalSnapshot(CodexGoalSourceStatus.Ready, sessionId,
                NativeGoal($"Goal for {sessionId}"), DateTimeOffset.UnixEpoch, null));
        }
    }

    private sealed class MutableConversationStore : IConversationStore
    {
        internal ConversationOverlay Overlay { get; set; } = ConversationOverlay.Disabled;
        public Task<ConversationOverlay> ReadAsync(string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(Overlay);
        public Task SetSharingAsync(string workspaceRoot, bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpsertAsync(string workspaceRoot, ConversationMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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
