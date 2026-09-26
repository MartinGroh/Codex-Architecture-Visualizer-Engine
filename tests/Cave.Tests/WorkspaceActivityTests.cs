using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Workspaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cave.Tests;

/// <summary>Verifies the bounded external activity projection and its HTTP contract.</summary>
public sealed class WorkspaceActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies bounded deterministic selection without changing identities or conflating provenance.</summary>
    [Fact]
    public async Task ProjectionBoundsAndOrdersAgentsWithoutSemanticGraph()
    {
        var agents = Enumerable.Range(0, 40).Select(index => Agent(index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), index < 2
            ? AgentWorkState.Active : AgentWorkState.Idle, Now.AddSeconds(index))).ToArray();
        var store = new StubActivityStore(new(agents, [], [], null, [], new string('e', 700))
        {
            SourceStatus = AgentActivitySourceStatus.Degraded,
        });
        var service = new WorkspaceActivityService(store, new FixedClock());
        var response = await service.ReadAsync(new("workspace", Path.GetTempPath(), Now), CancellationToken.None);

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal(40, response.TotalAgentCount);
        Assert.Equal(32, response.Agents.Count);
        Assert.Equal(["01", "00", "39"], response.Agents.Take(3).Select(agent => agent.AgentId));
        Assert.Equal(160, response.Agents[0].Summary!.Length);
        Assert.Equal(64, response.Agents[0].AgentType.Length);
        Assert.Equal(512, response.Error!.Length);
        Assert.Equal(Now.AddSeconds(39), response.SourceUpdatedAtUtc);
        Assert.Equal(Now, response.GeneratedAtUtc);
        Assert.Equal(AgentActivitySourceStatus.Degraded, response.SourceStatus);
        Assert.Equal(AgentActivityEvidenceKind.Observed, response.Agents[0].Evidence);
        Assert.Equal(AgentActivityEvidenceKind.Declared, response.Agents[0].SummaryEvidence);
        Assert.NotNull(store.Graph);
        Assert.Empty(store.Graph.Nodes);
        Assert.Empty(store.Graph.Relations);
    }

    /// <summary>Verifies missing evidence stays missing and tied identities have deterministic order.</summary>
    [Fact]
    public async Task ProjectionPreservesUnknownEvidenceAndEmptySource()
    {
        var empty = new WorkspaceActivityService(new StubActivityStore(AgentActivityOverlay.Empty), new FixedClock());
        var response = await empty.ReadAsync(new("workspace", Path.GetTempPath(), Now), CancellationToken.None);
        Assert.Null(response.SourceUpdatedAtUtc);
        Assert.Equal(AgentActivitySourceStatus.Unobserved, response.SourceStatus);
        Assert.Empty(response.Agents);

        var unknown = Agent("b", AgentWorkState.Active, Now) with { Evidence = null, SummaryEvidence = null };
        var store = new StubActivityStore(new([unknown, unknown with { AgentId = "a" }], [], [], null, [], null));
        var tied = await new WorkspaceActivityService(store, new FixedClock())
            .ReadAsync(new("workspace", Path.GetTempPath(), Now), CancellationToken.None);
        Assert.Equal(["a", "b"], tied.Agents.Select(agent => agent.AgentId));
        Assert.All(tied.Agents, agent => Assert.Null(agent.Evidence));
    }

    /// <summary>Verifies oversized identities fail visibly instead of silently colliding after truncation.</summary>
    [Fact]
    public async Task ProjectionRejectsOversizedIdentity()
    {
        var store = new StubActivityStore(new([Agent(new string('i', 129), AgentWorkState.Active, Now)], [], [], null, [], null));
        await Assert.ThrowsAsync<InvalidDataException>(() => new WorkspaceActivityService(store, new FixedClock())
            .ReadAsync(new("workspace", Path.GetTempPath(), Now), CancellationToken.None));
    }

    /// <summary>Verifies the source cancellation boundary is propagated.</summary>
    [Fact]
    public async Task ProjectionPropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorkspaceActivityService(new StubActivityStore(AgentActivityOverlay.Empty), new FixedClock())
                .ReadAsync(new("workspace", Path.GetTempPath(), Now), source.Token));
    }

    /// <summary>Verifies current lifecycle evidence does not inherit the provenance of an older declaration.</summary>
    [Fact]
    public async Task CanonicalReducerSeparatesLatestEvidenceFromSummaryEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-feed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileAgentActivityStore(new FixedClock());
            await store.AppendScopeAsync(root, new("agent", "worker", true, AgentWorkState.Active,
                "Declared work", [], [], [], []), CancellationToken.None);
            var declared = await store.ReadAsync(root, ArchitectureGraph.Create([], []), CancellationToken.None);
            Assert.Equal(AgentActivityEvidenceKind.Declared, Assert.Single(declared.Agents).Evidence);

            var payload = new
            {
                schemaVersion = 1,
                eventId = "observed",
                kind = "PostToolUse",
                occurredAtUtc = Now.AddSeconds(1),
                agentId = "agent",
                agentType = "worker",
                isSubagent = true,
                workspaceRoot = root,
                toolName = "read_file",
                phase = "Reading",
                paths = Array.Empty<string>(),
            };
            await File.WriteAllTextAsync(Path.Combine(FileAgentActivityStore.GetActivityDirectory(root), "observed.json"),
                JsonSerializer.Serialize(payload));
            var projected = Assert.Single((await store.ReadAsync(root, ArchitectureGraph.Create([], []), CancellationToken.None)).Agents);
            Assert.Equal(AgentActivityEvidenceKind.Observed, projected.Evidence);
            Assert.Equal(AgentActivityEvidenceKind.Declared, projected.SummaryEvidence);
            Assert.Equal(AgentActivityPhase.Reading, projected.Phase);
            Assert.Equal("Declared work", projected.Summary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Verifies HTTP routing exposes only compact activity and no static/conversation state.</summary>
    [Fact]
    public async Task EndpointReturnsVersionedActivityWithoutStartingSemanticProvider()
    {
        using var fixture = new FeedHost();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/activity?workspace=" + fixture.WorkspaceId);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(fixture.WorkspaceId, root.GetProperty("workspaceId").GetString());
        Assert.False(root.TryGetProperty("graph", out _));
        Assert.False(root.TryGetProperty("conversation", out _));
        Assert.Equal("Observed", root.GetProperty("agents")[0].GetProperty("evidence").GetString());
        Assert.Equal("Declared", root.GetProperty("agents")[0].GetProperty("summaryEvidence").GetString());
        Assert.NotNull(fixture.Activity.Graph);
        Assert.Empty(fixture.Activity.Graph.Nodes);
    }

    /// <summary>Verifies explicit workspace selection and unavailable registrations fail with actionable status.</summary>
    [Theory]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("?workspace=unknown", HttpStatusCode.NotFound)]
    [InlineData("?workspace=missing", HttpStatusCode.Gone)]
    public async Task EndpointRejectsMissingUnknownAndUnavailableWorkspace(string query, HttpStatusCode expected)
    {
        using var fixture = new FeedHost();
        using var client = fixture.Factory.CreateClient();
        if (query.EndsWith("missing", StringComparison.Ordinal))
        {
            Directory.Delete(fixture.WorkspaceRoot, recursive: true);
            query = "?workspace=" + fixture.WorkspaceId;
        }
        using var response = await client.GetAsync("/api/activity" + query);
        Assert.Equal(expected, response.StatusCode);
    }

    private static AgentActivity Agent(string id, AgentWorkState state, DateTimeOffset time) =>
        new(id, new string('t', 100), false, state, AgentActivityPhase.Working, new string('s', 200),
            true, true, Now.AddMinutes(-1), time)
        {
            Evidence = AgentActivityEvidenceKind.Observed,
            SummaryEvidence = AgentActivityEvidenceKind.Declared,
        };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubActivityStore(AgentActivityOverlay overlay) : IAgentActivityStore
    {
        public ArchitectureGraph? Graph { get; private set; }

        public Task<AgentActivityOverlay> ReadAsync(string workspaceRoot, ArchitectureGraph graph, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Graph = graph;
            return Task.FromResult(overlay);
        }

        public Task AppendScopeAsync(string workspaceRoot, AgentScopeDeclaration declaration, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FeedHost : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cave-feed-host-tests", Guid.NewGuid().ToString("N"));
        public string WorkspaceRoot { get; }
        public string WorkspaceId { get; }
        public StubActivityStore Activity { get; } = new(new([Agent("agent", AgentWorkState.Active, Now)], [], [], null, [], null)
        {
            SourceStatus = AgentActivitySourceStatus.Ready,
        });
        public WebApplicationFactory<Program> Factory { get; }

        public FeedHost()
        {
            WorkspaceRoot = Path.Combine(_root, "Workspace");
            Directory.CreateDirectory(Path.Combine(WorkspaceRoot, ".codegraph"));
            File.WriteAllBytes(Path.Combine(WorkspaceRoot, ".codegraph", "codegraph.db"), []);
            WorkspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(WorkspaceRoot);
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseSetting("Cave:SemanticProvider", "CodeGraph")
                .UseSetting("Cave:WorkspaceRoot", WorkspaceRoot)
                .UseSetting("Cave:WorkspaceCatalogRoot", Path.Combine(_root, "catalog"))
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAgentActivityStore>();
                    services.AddSingleton<IAgentActivityStore>(Activity);
                }));
        }

        public void Dispose()
        {
            Factory.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
