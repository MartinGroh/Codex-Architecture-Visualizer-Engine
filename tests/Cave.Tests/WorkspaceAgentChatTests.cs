using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Demo;

namespace Cave.Tests;

/// <summary>Protects exact task membership and revocable browser conversation sharing.</summary>
public sealed class WorkspaceAgentChatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly AgentChatSharingPeriod Period = new(Guid.NewGuid(), Now.AddMinutes(-1));
    private static readonly WorkspaceCatalogEntry Workspace = new("workspace", Path.GetTempPath(), Now);
    private static readonly string RootId = AgentDisplayNames.PublicId("session:root");

    /// <summary>Native descendants and hook rows correlate by exact identity even when display names repeat.</summary>
    [Fact]
    public async Task ProjectsOnlyTheSelectedTaskAndItsVerifiedDescendants()
    {
        var source = new ChatSource();
        var (service, store) = CreateService(source);
        var snapshot = await service.ReadAsync(Workspace, RootId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(AgentChatSharing.Shared, snapshot.Sharing);
        Assert.Equal(Period.Id.ToString("N"), snapshot.PeriodId);
        Assert.Equal(3, snapshot.Agents.Count);
        Assert.Equal(RootId, snapshot.Agents[0].Id);
        Assert.Equal("Root published", Assert.Single(snapshot.Agents[0].Messages).Text);
        var child = Assert.Single(snapshot.Agents, agent => agent.Id == AgentDisplayNames.PublicId("child"));
        var nested = Assert.Single(snapshot.Agents, agent => agent.Id == AgentDisplayNames.PublicId("nested"));
        Assert.Equal(child.DisplayName, nested.DisplayName);
        Assert.Equal(RootId, child.ParentId);
        Assert.Equal(child.Id, nested.ParentId);
        Assert.Equal("Nested published", Assert.Single(nested.Messages).Text);
        Assert.Equal(["child", "nested", "root"], source.Candidates.Order(StringComparer.Ordinal));

        var artifact = Assert.Single(Assert.Single(child.Messages).Artifacts);
        Assert.NotNull(await service.ReadArtifactAsync(Workspace, RootId, artifact.Id, CancellationToken.None));
        Assert.Null(await service.ReadArtifactAsync(Workspace, AgentDisplayNames.PublicId("session:other"), artifact.Id, CancellationToken.None));
        store.Sharing = new(true, new AgentChatSharingPeriod(Guid.NewGuid(), Now.AddSeconds(1)));
        Assert.Null(await service.ReadArtifactAsync(Workspace, RootId, artifact.Id, CancellationToken.None));
    }

    /// <summary>Late replies and bytes from a revoked or replaced period cannot reach the browser.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharingChangesWhileTheSourceReadsDiscardItsResults(bool enabledAgain)
    {
        var source = new ChatSource();
        var (service, store) = CreateService(source);
        source.DuringRead = () => store.Sharing = enabledAgain
            ? new ConversationSharingState(true, new AgentChatSharingPeriod(Guid.NewGuid(), Now.AddSeconds(1)))
            : new ConversationSharingState(false, null);

        var snapshot = await service.ReadAsync(Workspace, RootId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(AgentChatSharing.Private, snapshot.Sharing);
        Assert.Empty(snapshot.Agents);
    }

    /// <summary>Legacy workspaces keep the existing public hook evidence without a native history read.</summary>
    [Fact]
    public async Task LegacyOptInDoesNotBackfillNativeHistory()
    {
        var source = new ChatSource();
        var (service, store) = CreateService(source);
        store.Sharing = new(true, null);

        var snapshot = await service.ReadAsync(Workspace, RootId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(AgentChatSharing.Legacy, snapshot.Sharing);
        Assert.Equal("Root hook", Assert.Single(snapshot.Agents[0].Messages).Text);
        Assert.Empty(source.Candidates);
    }

    private static (WorkspaceAgentChatService Service, ConversationStore Conversations) CreateService(ChatSource source)
    {
        var clock = new FixedClock();
        var activity = new ActivityStore();
        var conversations = new ConversationStore();
        var telemetry = new DemoAgentTelemetry(clock, Workspace.WorkspaceRoot);
        return (new WorkspaceAgentChatService(
            new WorkspaceActivityService(activity, clock, new AgentTelemetryService(telemetry, telemetry, activity, clock)),
            conversations, source, clock, AgentFlowSourceMode.Live), conversations);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ActivityStore : IAgentActivityStore
    {
        public Task<AgentActivityOverlay> ReadAsync(string workspaceRoot, ArchitectureGraph graph, CancellationToken cancellationToken) =>
            Task.FromResult(AgentActivityOverlay.Empty with
            {
                Agents =
                [
                    Agent("session:root", null),
                    Agent("child", "session:root"),
                    Agent("nested", "session:child"),
                    Agent("session:other", null),
                    Agent("other-child", "session:other"),
                ],
            });

        public Task AppendScopeAsync(string workspaceRoot, AgentScopeDeclaration declaration, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static AgentActivity Agent(string id, string? parent) =>
            new(id, "Codex", parent is not null, AgentWorkState.Active, AgentActivityPhase.Working, null,
                true, false, Now.AddMinutes(-2), Now) { ParentAgentId = parent, DisplayName = "Same alias" };
    }

    private sealed class ConversationStore : IConversationStore
    {
        internal ConversationSharingState Sharing { get; set; } = new(true, Period);

        public Task<ConversationSharingState> ReadSharingAsync(string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(Sharing);

        public Task<ConversationOverlay> ReadAsync(string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationOverlay(Sharing.Enabled, ConversationSourceStatus.Ready,
            [
                new("root-hook", "root", "turn", null, null, false, ConversationRole.User, ConversationMessageKind.Prompt,
                    "Root hook", false, false, Now),
                new("other-hook", "other", "turn", null, null, false, ConversationRole.User, ConversationMessageKind.Prompt,
                    "Foreign hook", false, false, Now),
            ], ConversationControl.Unavailable, null));

        public Task SetSharingAsync(string workspaceRoot, bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpsertAsync(string workspaceRoot, ConversationMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ChatSource : ICodexAgentChatSource
    {
        internal IReadOnlyCollection<string> Candidates { get; private set; } = [];
        internal Action? DuringRead { get; set; }

        public Task<CodexAgentChatBatch> ReadAsync(string rootThreadId, IReadOnlyCollection<string> candidateThreadIds,
            DateTimeOffset sharedSinceUtc, CancellationToken cancellationToken)
        {
            Candidates = candidateThreadIds.ToArray();
            DuringRead?.Invoke();
            return Task.FromResult(new CodexAgentChatBatch(rootThreadId, CodexAgentChatStatus.Available,
            [
                new("root", null, CodexAgentChatStatus.Available,
                [
                    new("past", "old-turn", null, ConversationRole.User, ConversationMessageKind.Prompt,
                        Now.AddMinutes(-8), "Before sharing", false, []),
                    new("published", "turn", null, ConversationRole.User, ConversationMessageKind.Prompt, Now, "Root published", false, []),
                ]),
                new("child", "root", CodexAgentChatStatus.Available,
                    [Message("Child published", [new("image/png", [1, 2, 3])])]),
                new("nested", "child", CodexAgentChatStatus.Available, [Message("Nested published", [])]),
                new("other-child", "other", CodexAgentChatStatus.Available, [Message("Foreign published", [])]),
            ]));
        }

        private static CodexAgentChatEntry Message(string text, IReadOnlyList<CodexAgentChatImage> images) =>
            new("child-item", "child-turn", "turn", ConversationRole.Assistant, ConversationMessageKind.Final, Now, text, false, images);
    }
}
