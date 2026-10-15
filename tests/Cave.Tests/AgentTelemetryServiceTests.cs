using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>Verifies bounded fair collection and exact-identity presentation independently from native I/O.</summary>
public sealed class AgentTelemetryServiceTests
{
    /// <summary>More than 32 active agents rotate fairly without querying display aliases or counting a shared agent twice.</summary>
    [Fact]
    public async Task CollectorRotatesActiveAgentsAndUnionsWorkspaceMembership()
    {
        var clock = new Clock();
        var activity = new Activity(Enumerable.Range(0, 50).Select(index => Agent("child-" + index, true, clock.Now)).ToArray());
        var source = new Source(clock);
        var store = new TestTelemetry.MemoryStore(new([], null));
        var service = new AgentTelemetryService(store, source, activity, clock);
        WorkspaceCatalogEntry[] projects = [new("one", "one", clock.Now), new("two", "two", clock.Now)];
        await service.CollectAsync(projects, CancellationToken.None);
        clock.Now += TimeSpan.FromSeconds(15);
        await service.CollectAsync(projects, CancellationToken.None);
        Assert.All(source.Requests, ids => Assert.InRange(ids.Length, 1, 32));
        Assert.Equal(50, source.Requests.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(50, store.State.Records.Count);
        Assert.All(store.State.Records, record => Assert.Equal(2, record.WorkspaceIds.Count));
        var stats = await service.ReadStatisticsAsync(null, null, new Dictionary<string, string>(), CancellationToken.None);
        Assert.Equal(50, stats.Totals.AgentCount);
        Assert.Equal("Shared across projects", Assert.Single(stats.Projects).Label);
    }

    /// <summary>Presentation never queries native state or gives an unknown child the parent's settings.</summary>
    [Fact]
    public async Task EnrichmentUsesEachExactIdentityAndClearsStaleConfiguration()
    {
        var now = DateTimeOffset.UtcNow;
        var parent = Record("main", null, now, "gpt-6.1-sol");
        var child = Record("child", "main", now, "gpt-6-astra");
        var stale = Record("stale", "main", now.AddMinutes(-3), "gpt-6-luna");
        var service = TestTelemetry.Create(new([parent, child, stale], null));
        var result = await service.EnrichAsync(new([Agent("session:main", false, now), Agent("child", true, now),
            Agent("unknown-child", true, now), Agent("stale", true, now)], [], [], null, [], null), CancellationToken.None);
        Assert.Equal("gpt-6.1-sol", result.Agents[0].Configuration?.Model);
        Assert.Equal("gpt-6-astra", result.Agents[1].Configuration?.Model);
        Assert.Null(result.Agents[2].Configuration);
        Assert.Null(result.Agents[3].Configuration);
        Assert.Equal(result.Agents[0].StatisticsTaskId, result.Agents[1].StatisticsTaskId);
        Assert.Equal(AgentDisplayNames.PublicId("main"), result.Agents[0].StatisticsTaskId);
    }

    private static AgentTelemetryRecord Record(string id, string? parent, DateTimeOffset now, string model) =>
        new(id, parent, ["one"], parent is not null, now, now, now, true, new(model, "high"), null, false, null, 0, false, [], null);

    private static AgentActivity Agent(string id, bool child, DateTimeOffset now) =>
        new(id, child ? "Review" : "Main", child, AgentWorkState.Active, AgentActivityPhase.Working, null, true, false, now, now);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Activity(AgentActivity[] agents) : IAgentActivityStore
    {
        public Task<AgentActivityOverlay> ReadAsync(string root, ArchitectureGraph graph, CancellationToken token) =>
            Task.FromResult(new AgentActivityOverlay(agents, [], [], null, [], null));
        public Task AppendScopeAsync(string root, AgentScopeDeclaration declaration, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Source(Clock clock) : IAgentTelemetrySource
    {
        internal readonly List<string[]> Requests = [];
        public Task<IReadOnlyList<AgentTelemetrySample>> ReadAsync(IReadOnlyList<string> ids, CancellationToken token)
        {
            Requests.Add(ids.ToArray());
            return Task.FromResult<IReadOnlyList<AgentTelemetrySample>>(ids.Select(id => new AgentTelemetrySample(id, "main", new("gpt-6.1-sol", "high"),
                null, null, 0, false, clock.Now, null)).ToArray());
        }
    }
}
