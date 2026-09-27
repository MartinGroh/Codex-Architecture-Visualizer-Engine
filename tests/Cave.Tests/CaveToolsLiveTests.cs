using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Conversation;
using Cave.Infrastructure.Demo;
using Cave.Infrastructure.Live;
using Cave.Infrastructure.Sample;
using Cave.Infrastructure.Workspaces;
using Cave.Mcp;

namespace Cave.Tests;

/// <summary>
/// Verifies the live contract shared by the CAVE MCP App and its private poll tool.
/// </summary>
public sealed class CaveToolsLiveTests
{
    /// <summary>
    /// Verifies that a client read publishes an expired activity state without requiring another file event.
    /// </summary>
    [Fact]
    public async Task CurrentSnapshotReevaluatesActivityFreshnessWithoutFileChange()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var activityStore = new FileAgentActivityStore(timeProvider);
            await activityStore.AppendScopeAsync(
                workspaceRoot,
                new AgentScopeDeclaration(
                    "agent-sub",
                    "implementation",
                    IsSubagent: true,
                    AgentWorkState.Active,
                    "Implementing live indicators",
                    [],
                    [],
                    [],
                    []),
                CancellationToken.None);
            var snapshotService = new ArchitectureSnapshotService(
                new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                activityStore,
                new WorkspaceConversationService(new FileConversationStore(), new DemoCodexGoalProvider(timeProvider), timeProvider),
                timeProvider);
            await using var monitor = new WorkspaceGraphMonitorFactory(snapshotService, timeProvider)
                .Create(workspaceRoot);

            var initial = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.Equal(AgentWorkState.Active, Assert.Single(initial.Snapshot.Activity.Agents).State);
            var unchangedActive = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.Equal(initial.Version, unchangedActive.Version);

            timeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
            var refreshed = await monitor.GetCurrentAsync(CancellationToken.None);

            Assert.True(refreshed.Version > initial.Version);
            Assert.True(refreshed.ActivityChanged);
            Assert.Empty(refreshed.ChangedPaths);
            Assert.Equal(AgentWorkState.Idle, Assert.Single(refreshed.Snapshot.Activity.Agents).State);
            var unchangedIdle = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.Equal(refreshed.Version, unchangedIdle.Version);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a burst of hook events cannot be lost between channel draining and refresh.
    /// </summary>
    [Fact]
    public async Task MonitorEventuallyPublishesLatestActivityFromRapidBurst()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var activityStore = new FileAgentActivityStore(timeProvider);
            var snapshotService = new ArchitectureSnapshotService(
                new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                activityStore,
                new WorkspaceConversationService(new FileConversationStore(), new DemoCodexGoalProvider(timeProvider), timeProvider),
                timeProvider);
            await using var monitor = new WorkspaceGraphMonitorFactory(snapshotService, timeProvider)
                .Create(workspaceRoot);
            var current = await monitor.GetCurrentAsync(CancellationToken.None);

            for (var index = 1; index <= 25; index++)
            {
                timeProvider.Advance(TimeSpan.FromMilliseconds(1));
                await activityStore.AppendScopeAsync(
                    workspaceRoot,
                    new AgentScopeDeclaration(
                        "agent-main",
                        "implementation",
                        IsSubagent: false,
                        AgentWorkState.Active,
                        $"Burst {index}",
                        [index % 2 == 0 ? "Cave.Application" : "Cave.Host"],
                        [],
                        [],
                        []),
                    CancellationToken.None);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (current.Snapshot.Activity.Agents.SingleOrDefault()?.Summary != "Burst 25")
            {
                current = await monitor.WaitForUpdateAsync(
                    current.Version,
                    TimeSpan.FromMilliseconds(500),
                    timeout.Token);
            }

            Assert.True(current.ActivityChanged);
            Assert.Equal("Burst 25", Assert.Single(current.Snapshot.Activity.Agents).Summary);
            Assert.Contains(
                current.Snapshot.Activity.Nodes,
                node => node.AgentId == "agent-main" && node.NodeId == "project:Cave.Host");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that changing the opt-in conversation setting refreshes only live overlays.
    /// </summary>
    [Fact]
    public async Task MonitorPublishesConversationSharingChange()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var conversationStore = new FileConversationStore();
            var snapshotService = new ArchitectureSnapshotService(
                new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                new FileAgentActivityStore(TimeProvider.System),
                new WorkspaceConversationService(conversationStore, new DemoCodexGoalProvider(TimeProvider.System), TimeProvider.System),
                TimeProvider.System);
            await using var monitor = new WorkspaceGraphMonitorFactory(snapshotService, TimeProvider.System)
                .Create(workspaceRoot);
            var initial = await monitor.GetCurrentAsync(CancellationToken.None);

            await conversationStore.SetSharingAsync(workspaceRoot, enabled: true, CancellationToken.None);
            var update = await monitor.WaitForUpdateAsync(
                initial.Version,
                TimeSpan.FromSeconds(5),
                CancellationToken.None);

            Assert.True(update.ConversationChanged);
            Assert.True(update.Snapshot.Conversation.SharingEnabled);
            Assert.False(update.ActivityChanged);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a source-file change advances the app subscription version.
    /// </summary>
    [Fact]
    public async Task PollReturnsNewVersionAfterSourceChange()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var snapshotService = new ArchitectureSnapshotService(
                new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                new FileAgentActivityStore(TimeProvider.System),
                new WorkspaceConversationService(new FileConversationStore(), new DemoCodexGoalProvider(TimeProvider.System), TimeProvider.System),
                TimeProvider.System);
            var factory = new WorkspaceGraphMonitorFactory(snapshotService, TimeProvider.System);
            await using var registry = new WorkspaceGraphMonitorRegistry(
                factory,
                new MachineWorkspaceCatalogStore(
                    Path.Combine(workspaceRoot, ".catalog"),
                    TimeProvider.System));
            var tools = CreateTools(registry);

            var initialResult = await tools.ShowArchitectureAsync(workspaceRoot, CancellationToken.None);
            var initial = initialResult.StructuredContent!.Value;
            var subscriptionId = initial.GetProperty("subscriptionId").GetString()!;
            var initialVersion = initial.GetProperty("update").GetProperty("version").GetInt64();

            await File.WriteAllTextAsync(
                Path.Combine(workspaceRoot, "LiveProbe.cs"),
                "internal sealed class LiveProbe;",
                CancellationToken.None);

            var pollResult = await tools.PollArchitectureAsync(
                subscriptionId,
                initialVersion,
                CancellationToken.None);
            var update = pollResult.StructuredContent!.Value.GetProperty("update");

            Assert.True(update.GetProperty("version").GetInt64() > initialVersion);
            Assert.Contains(
                "LiveProbe.cs",
                update.GetProperty("changedPaths").EnumerateArray().Select(path => path.GetString()));
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that Git branch and commit control-file changes refresh the snapshot without leaking internal paths.
    /// </summary>
    [Fact]
    public async Task PollReturnsNewVersionAfterGitIdentityChange()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        var refsDirectory = Path.Combine(workspaceRoot, ".git", "refs", "heads");
        Directory.CreateDirectory(refsDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspaceRoot, ".git", "HEAD"), "ref: refs/heads/main\n");
            var referencePath = Path.Combine(refsDirectory, "main");
            await File.WriteAllTextAsync(referencePath, "abc123\n");
            var snapshotService = new ArchitectureSnapshotService(
                new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                new FileAgentActivityStore(TimeProvider.System),
                new WorkspaceConversationService(new FileConversationStore(), new DemoCodexGoalProvider(TimeProvider.System), TimeProvider.System),
                TimeProvider.System);
            var factory = new WorkspaceGraphMonitorFactory(snapshotService, TimeProvider.System);
            await using var registry = new WorkspaceGraphMonitorRegistry(
                factory,
                new MachineWorkspaceCatalogStore(
                    Path.Combine(workspaceRoot, ".catalog"),
                    TimeProvider.System));
            var tools = CreateTools(registry);

            var initialResult = await tools.ShowArchitectureAsync(workspaceRoot, CancellationToken.None);
            var initial = initialResult.StructuredContent!.Value;
            var subscriptionId = initial.GetProperty("subscriptionId").GetString()!;
            var initialVersion = initial.GetProperty("update").GetProperty("version").GetInt64();

            await File.WriteAllTextAsync(referencePath, "def456\n");

            var pollResult = await tools.PollArchitectureAsync(
                subscriptionId,
                initialVersion,
                CancellationToken.None);
            var update = pollResult.StructuredContent!.Value.GetProperty("update");

            Assert.True(update.GetProperty("version").GetInt64() > initialVersion);
            Assert.Empty(update.GetProperty("changedPaths").EnumerateArray());
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    /// <summary>Native goal content changes publish through the canonical live feed without file changes.</summary>
    [Fact]
    public async Task MonitorPublishesGoalChangesButIgnoresRetrievalTimestampOnly()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var conversations = new MutableConversationStore
            {
                Overlay = new ConversationOverlay(true, ConversationSourceStatus.Ready, [],
                    ConversationControl.Unavailable with { SessionId = "task-a" }, null),
            };
            var goals = new MutableGoalProvider();
            var service = new ArchitectureSnapshotService(new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                new FileAgentActivityStore(TimeProvider.System),
                new WorkspaceConversationService(conversations, goals, TimeProvider.System), TimeProvider.System);
            await using var monitor = new WorkspaceGraphMonitorFactory(service, TimeProvider.System).Create(workspaceRoot);
            var initial = await monitor.GetCurrentAsync(CancellationToken.None);
            goals.Result = goals.Result with { RetrievedAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(16) };
            var sameGoal = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.Equal(initial.Version, sameGoal.Version);

            goals.Result = goals.Result with { Goal = goals.Result.Goal! with { TokensUsed = 25 } };
            var changed = await monitor.WaitForUpdateAsync(initial.Version, TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.True(changed.Version > initial.Version);
            Assert.True(changed.ConversationChanged);
            Assert.False(changed.ActivityChanged);
            Assert.Empty(changed.ChangedPaths);
            Assert.Equal(25L, changed.Snapshot.Conversation.Goal?.Goal?.TokensUsed);

            conversations.Overlay = conversations.Overlay with { SharingEnabled = false };
            var disabled = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.True(disabled.ConversationChanged);
            Assert.Null(disabled.Snapshot.Conversation.Goal);
        }
        finally { Directory.Delete(workspaceRoot, recursive: true); }
    }

    /// <summary>Exact task/control changes remain observable even with no shared conversation or goal.</summary>
    [Fact]
    public async Task MonitorPublishesControlIdentityAndDeliveryChangesWhileSharingDisabled()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "cave-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var conversations = new MutableConversationStore
            {
                Overlay = ConversationOverlay.Disabled with
                {
                    Control = ConversationControl.Unavailable with { SessionId = "task-a" },
                },
            };
            var service = new ArchitectureSnapshotService(new SampleSemanticIndex(),
                new GitDeltaService(new EmptyGitDeltaProvider(), GitBaselineRequest.Upstream),
                new FileAgentActivityStore(TimeProvider.System),
                new WorkspaceConversationService(conversations, new DemoCodexGoalProvider(TimeProvider.System), TimeProvider.System),
                TimeProvider.System);
            await using var monitor = new WorkspaceGraphMonitorFactory(service, TimeProvider.System).Create(workspaceRoot);
            var initial = await monitor.GetCurrentAsync(CancellationToken.None);
            conversations.Overlay = conversations.Overlay with
            {
                Control = new ConversationControl("task-b", "turn-b", ConversationControlState.Queued, true,
                    [new ConversationDelivery("message-b", "task-b", null, ConversationDeliveryState.Queued,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null)], null),
            };
            var switched = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.True(switched.Version > initial.Version);
            Assert.True(switched.ConversationChanged);
            Assert.Equal("task-b", switched.Snapshot.Conversation.Control.SessionId);
            Assert.Null(switched.Snapshot.Conversation.Goal);

            var queued = Assert.Single(conversations.Overlay.Control.Deliveries);
            conversations.Overlay = conversations.Overlay with
            {
                Control = conversations.Overlay.Control with
                {
                    Deliveries = [queued with { State = ConversationDeliveryState.Completed }],
                },
            };
            var completed = await monitor.GetCurrentAsync(CancellationToken.None);
            Assert.True(completed.Version > switched.Version);
            Assert.Equal(ConversationDeliveryState.Completed,
                Assert.Single(completed.Snapshot.Conversation.Control.Deliveries).State);
        }
        finally { Directory.Delete(workspaceRoot, recursive: true); }
    }

    private sealed class MutableGoalProvider : ICodexGoalProvider
    {
        internal CodexGoalSnapshot Result { get; set; } = new(CodexGoalSourceStatus.Ready, "task-a",
            new CodexGoal("Shared workspace goal", CodexGoalStatus.Active, null, 0, 0, 123, 456),
            DateTimeOffset.UnixEpoch, null);
        public Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
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

    private sealed class EmptyGitDeltaProvider : IGitDeltaProvider
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
                []));
        }
    }

    private static CaveTools CreateTools(WorkspaceGraphMonitorRegistry registry)
    {
        var conversationStore = new FileConversationStore();
        return new CaveTools(
            registry,
            new FileAgentActivityStore(TimeProvider.System),
            conversationStore,
            new CaveInfoService(new EmptyUsageProvider()));
    }

    private sealed class EmptyUsageProvider : ICodexUsageProvider
    {
        public Task<CodexUsageSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CodexUsageSnapshot(
                CodexUsageStatus.Unavailable,
                null,
                [],
                [],
                DateTimeOffset.UnixEpoch,
                "Unavailable in this test."));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private readonly object _gate = new();

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan duration)
        {
            lock (_gate)
            {
                _now += duration;
            }
        }
    }
}
