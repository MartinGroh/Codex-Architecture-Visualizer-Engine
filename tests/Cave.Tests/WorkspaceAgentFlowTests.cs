using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Workspaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cave.Tests;

/// <summary>Verifies the graph-free Agent Flow projection, privacy boundaries and selected-workspace HTTP route.</summary>
public sealed class WorkspaceAgentFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The feed keeps bounded activity/provenance and exposes only opaque agent correlation keys.</summary>
    [Fact]
    public async Task ProjectionPreservesActivityBoundsAndSeparatesDeclaredFocus()
    {
        var agents = Enumerable.Range(0, 40).Select(index => Agent($"session:native-task-{index:D2}", index > 0,
            index % 2 == 0 ? AgentActivityEvidenceKind.Declared : AgentActivityEvidenceKind.Observed) with
        {
            UpdatedAtUtc = Now.AddSeconds(index),
            AgentType = new string('r', 100),
            Summary = new string('s', 200),
            ParentAgentId = index == 39 ? "session:parent-task" : null,
            LastObservedActivity = index == 39 ? "Validated the device feed" : null,
        }).ToArray();
        var activity = new RecordingActivityStore(new(agents, [], [], null, [], "C:\\private\\workspace: raw source error")
        {
            SourceStatus = AgentActivitySourceStatus.Degraded,
        });
        var conversations = new MutableConversationStore();
        var goals = new RecordingGoalProvider();

        var result = await CreateService(activity, conversations, goals).ReadAsync(Workspace(), CancellationToken.None);

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(AgentFlowSourceMode.Live, result.SourceMode);
        Assert.Equal(40, result.TotalAgentCount);
        Assert.Equal(32, result.Agents.Count);
        Assert.Equal(Now.AddSeconds(39), result.SourceUpdatedAtUtc);
        Assert.Equal(Now, result.GeneratedAtUtc);
        Assert.Equal(AgentActivitySourceStatus.Degraded, result.SourceStatus);
        var first = result.Agents[0];
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("session:native-task-39"))).ToLowerInvariant(), first.AgentId);
        Assert.Equal(AgentDisplayNames.Get("session:native-task-39", true), first.DisplayName);
        Assert.Equal(64, first.AgentType.Length);
        Assert.Equal(160, first.Summary!.Length);
        Assert.Null(first.CurrentFocus);
        Assert.Null(first.FocusEvidence);
        Assert.Equal(AgentActivityEvidenceKind.Observed, first.Evidence);
        Assert.Equal(AgentActivityEvidenceKind.Observed, first.SummaryEvidence);
        Assert.Equal(AgentDisplayNames.PublicId("session:parent-task"), first.ParentAgentId);
        Assert.Equal("Validated the device feed", first.LastObservedActivity);
        var declared = result.Agents[1];
        Assert.Equal(declared.Summary, declared.CurrentFocus);
        Assert.Equal(AgentActivityEvidenceKind.Declared, declared.FocusEvidence);
        Assert.Equal(AgentActivityEvidenceKind.Observed, declared.Evidence);
        Assert.Equal(AgentFlowGoalSourceStatus.Private, result.MainGoal.Status);
        Assert.Equal(0, goals.ReadCount);
        Assert.NotNull(activity.Graph);
        Assert.Empty(activity.Graph.Nodes);
        Assert.Empty(activity.Graph.Relations);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        Assert.DoesNotContain("session:native-task", json, StringComparison.Ordinal);
        Assert.DoesNotContain("session:parent-task", json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_CHAT_MESSAGE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw source error", json, StringComparison.Ordinal);
    }

    /// <summary>Goal privacy and availability remain distinct; a successful empty result means no goal.</summary>
    [Theory]
    [InlineData(false, "native-task", CodexGoalSourceStatus.Ready, true, AgentFlowGoalSourceStatus.Private)]
    [InlineData(true, null, CodexGoalSourceStatus.Ready, true, AgentFlowGoalSourceStatus.Unbound)]
    [InlineData(true, "native-task", CodexGoalSourceStatus.Unavailable, true, AgentFlowGoalSourceStatus.Unavailable)]
    [InlineData(true, "native-task", CodexGoalSourceStatus.Ready, false, AgentFlowGoalSourceStatus.Ready)]
    [InlineData(true, "native-task", CodexGoalSourceStatus.Ready, true, AgentFlowGoalSourceStatus.Ready)]
    public async Task MainGoalStatesAreExplicitAndNeverExposeTaskBinding(
        bool sharing, string? sessionId, CodexGoalSourceStatus status, bool hasGoal, AgentFlowGoalSourceStatus expected)
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(sharing, sessionId) };
        var goals = new RecordingGoalProvider
        {
            Result = new(status, "native-task", hasGoal ? Goal("Explicit public goal") : null, Now,
                status == CodexGoalSourceStatus.Unavailable ? "C:\\private\\codex.cmd: native-task" : null),
        };
        var service = CreateService(new RecordingActivityStore(AgentActivityOverlay.Empty), conversations, goals);

        var result = await service.ReadAsync(Workspace(), CancellationToken.None);

        Assert.Equal(expected, result.MainGoal.Status);
        if (expected == AgentFlowGoalSourceStatus.Ready && hasGoal)
        {
            Assert.Equal("Explicit public goal", result.MainGoal.Goal?.Objective);
            Assert.Equal(0L, result.MainGoal.Goal?.TokensUsed);
            Assert.Null(result.MainGoal.Goal?.TokenBudget);
            Assert.Equal(Now, result.MainGoal.RetrievedAtUtc);
        }
        else
        {
            Assert.Null(result.MainGoal.Goal);
        }

        Assert.Equal(sharing && sessionId is not null ? 1 : 0, goals.ReadCount);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        Assert.DoesNotContain("native-task", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_CHAT_MESSAGE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    /// <summary>Long objectives explicitly report truncation and never split a supplementary Unicode character.</summary>
    [Fact]
    public async Task SharedObjectiveIsBoundedWithValidUtf16()
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(true, "native-task") };
        var goals = new RecordingGoalProvider
        {
            Result = new(CodexGoalSourceStatus.Ready, "native-task", Goal(new string('a', 2_047) + "😀tail"), Now, null),
        };
        var response = await CreateService(new RecordingActivityStore(AgentActivityOverlay.Empty), conversations, goals)
            .ReadAsync(Workspace(), CancellationToken.None);

        var goal = Assert.IsType<WorkspaceAgentFlowGoal>(response.MainGoal.Goal);
        Assert.Equal(2_047, goal.Objective.Length);
        Assert.True(goal.IsTruncated);
        Assert.False(char.IsHighSurrogate(goal.Objective[^1]));
        Assert.Equal(123L, goal.CreatedAt);
        Assert.Equal(456L, goal.UpdatedAt);
    }

    /// <summary>Changing sharing or the exact task during lookup discards the older objective.</summary>
    [Theory]
    [InlineData(false, "native-task", AgentFlowGoalSourceStatus.Private)]
    [InlineData(true, "replacement-task", AgentFlowGoalSourceStatus.Unavailable)]
    public async Task InFlightReadUsesCanonicalPrivacyAndTaskRecheck(
        bool sharing, string sessionId, AgentFlowGoalSourceStatus expected)
    {
        var conversations = new MutableConversationStore { Overlay = Conversation(true, "native-task") };
        var goals = new RecordingGoalProvider
        {
            OnRead = () => conversations.Overlay = Conversation(sharing, sessionId),
        };
        var result = await CreateService(new RecordingActivityStore(AgentActivityOverlay.Empty), conversations, goals)
            .ReadAsync(Workspace(), CancellationToken.None);

        Assert.Equal(expected, result.MainGoal.Status);
        Assert.Null(result.MainGoal.Goal);
    }

    /// <summary>Cancellation propagates before any goal lookup and is not presented as an unavailable goal.</summary>
    [Fact]
    public async Task CancellationStopsFeedRead()
    {
        var goals = new RecordingGoalProvider();
        var service = CreateService(new RecordingActivityStore(AgentActivityOverlay.Empty), new MutableConversationStore(), goals);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(Workspace(), cancellation.Token));
        Assert.Equal(0, goals.ReadCount);
    }

    /// <summary>The thin endpoint uses opaque catalog selection and reports missing/unknown/deleted workspaces.</summary>
    [Theory]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("?workspace=unknown", HttpStatusCode.NotFound)]
    [InlineData("?workspace=gone", HttpStatusCode.Gone)]
    [InlineData("?workspace=raw-root", HttpStatusCode.NotFound)]
    public async Task EndpointRejectsInvalidSelection(string query, HttpStatusCode expected)
    {
        using var fixture = new FlowHost();
        using var client = fixture.Factory.CreateClient();
        if (query.EndsWith("gone", StringComparison.Ordinal))
        {
            Directory.Delete(fixture.WorkspaceRoot, recursive: true);
            query = "?workspace=" + fixture.WorkspaceId;
        }
        else if (query.EndsWith("raw-root", StringComparison.Ordinal))
        {
            query = "?workspace=" + Uri.EscapeDataString(fixture.WorkspaceRoot);
        }

        using var response = await client.GetAsync("/api/agent-flow" + query);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(0, fixture.Semantic.ReadCount);
    }

    /// <summary>HTTP serialization publishes only the light feed and the explicitly configured source mode.</summary>
    [Theory]
    [InlineData(false, "Live")]
    [InlineData(true, "Demo")]
    public async Task EndpointReturnsBoundedFeedWithoutSemanticAcquisition(bool demoMode, string expectedMode)
    {
        using var fixture = new FlowHost(demoMode);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/agent-flow?workspace=" + fixture.WorkspaceId);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.Equal(1, body.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expectedMode, body.RootElement.GetProperty("sourceMode").GetString());
        Assert.Equal("Ready", body.RootElement.GetProperty("mainGoal").GetProperty("status").GetString());
        Assert.Equal("Shared goal", body.RootElement.GetProperty("mainGoal").GetProperty("goal").GetProperty("objective").GetString());
        Assert.Equal(64, Assert.Single(body.RootElement.GetProperty("agents").EnumerateArray()).GetProperty("agentId").GetString()!.Length);
        Assert.Equal(0, fixture.Semantic.ReadCount);
        Assert.NotNull(fixture.Activity.Graph);
        Assert.Empty(fixture.Activity.Graph.Nodes);
        Assert.DoesNotContain("PRIVATE_CHAT_MESSAGE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("native-task", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.WorkspaceRoot, json, StringComparison.Ordinal);
        Assert.False(body.RootElement.TryGetProperty("graph", out _));
        Assert.False(body.RootElement.TryGetProperty("git", out _));
        Assert.False(body.RootElement.TryGetProperty("conversation", out _));
        Assert.False(body.RootElement.TryGetProperty("usage", out _));
    }

    private static WorkspaceAgentFlowService CreateService(
        IAgentActivityStore activity, IConversationStore conversations, ICodexGoalProvider goals) =>
        new(new WorkspaceActivityService(activity, new FixedClock()),
            new WorkspaceConversationService(conversations, goals, new FixedClock()), AgentFlowSourceMode.Live);

    private static WorkspaceCatalogEntry Workspace() => new("workspace", Path.GetTempPath(), Now);
    private static CodexGoal Goal(string objective) => new(objective, CodexGoalStatus.Active, null, 0, 15, 123, 456);

    private static AgentActivity Agent(string id, bool child, AgentActivityEvidenceKind summaryEvidence) =>
        new(id, "Review", child, AgentWorkState.Active, AgentActivityPhase.Reading, "Declared review focus", true, true,
            Now.AddMinutes(-1), Now)
        {
            Evidence = AgentActivityEvidenceKind.Observed,
            SummaryEvidence = summaryEvidence,
        };

    private static ConversationOverlay Conversation(bool enabled, string? sessionId) => new(enabled,
        enabled ? ConversationSourceStatus.Ready : ConversationSourceStatus.Disabled,
        [new ConversationMessage("event", "native-task", "private-turn", null, "Main", false, ConversationRole.User,
            ConversationMessageKind.Prompt, "PRIVATE_CHAT_MESSAGE", false, false, Now)],
        ConversationControl.Unavailable with { SessionId = sessionId }, null);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingActivityStore(AgentActivityOverlay overlay) : IAgentActivityStore
    {
        internal ArchitectureGraph? Graph { get; private set; }
        public Task<AgentActivityOverlay> ReadAsync(string workspaceRoot, ArchitectureGraph graph, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Graph = graph;
            return Task.FromResult(overlay);
        }
        public Task AppendScopeAsync(string workspaceRoot, AgentScopeDeclaration declaration, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class MutableConversationStore : IConversationStore
    {
        internal ConversationOverlay Overlay { get; set; } = ConversationOverlay.Disabled;
        public Task<ConversationOverlay> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Overlay);
        }
        public Task SetSharingAsync(string workspaceRoot, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertAsync(string workspaceRoot, ConversationMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingGoalProvider : ICodexGoalProvider
    {
        internal int ReadCount { get; private set; }
        internal Action? OnRead { get; init; }
        internal CodexGoalSnapshot Result { get; init; } = new(CodexGoalSourceStatus.Ready, "native-task", Goal("Shared goal"), Now, null);
        public Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("native-task", sessionId);
            ReadCount++;
            OnRead?.Invoke();
            return Task.FromResult(Result);
        }
    }

    private sealed class GuardSemanticIndex : ISemanticIndex
    {
        internal int ReadCount { get; private set; }
        public Task<SemanticIndexResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            ReadCount++;
            throw new InvalidOperationException("Agent Flow must not acquire a semantic graph.");
        }
    }

    private sealed class FlowHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cave-agent-flow-tests", Guid.NewGuid().ToString("N"));
        internal string WorkspaceRoot { get; }
        internal string WorkspaceId { get; }
        internal GuardSemanticIndex Semantic { get; } = new();
        internal RecordingActivityStore Activity { get; } = new(new([Agent("session:native-task", false,
            AgentActivityEvidenceKind.Declared)], [], [], null, [], null) { SourceStatus = AgentActivitySourceStatus.Ready });
        internal WebApplicationFactory<Program> Factory { get; }

        internal FlowHost(bool demoMode = false)
        {
            WorkspaceRoot = Path.Combine(_root, "Workspace");
            Directory.CreateDirectory(Path.Combine(WorkspaceRoot, ".codegraph"));
            File.WriteAllBytes(Path.Combine(WorkspaceRoot, ".codegraph", "codegraph.db"), []);
            WorkspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(WorkspaceRoot);
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseSetting("Cave:DemoMode", demoMode.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .UseSetting("Cave:WorkspaceRoot", WorkspaceRoot)
                .UseSetting("Cave:WorkspaceCatalogRoot", Path.Combine(_root, "catalog"))
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAgentActivityStore>();
                    services.AddSingleton<IAgentActivityStore>(Activity);
                    services.RemoveAll<ISemanticIndex>();
                    services.AddSingleton<ISemanticIndex>(Semantic);
                    services.RemoveAll<IConversationStore>();
                    services.AddSingleton<IConversationStore>(new MutableConversationStore { Overlay = Conversation(true, "native-task") });
                    services.RemoveAll<ICodexGoalProvider>();
                    services.AddSingleton<ICodexGoalProvider>(new RecordingGoalProvider());
                }));
        }

        public void Dispose()
        {
            Factory.Dispose();
            if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
        }
    }
}
