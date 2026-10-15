using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Activity;

namespace Cave.Tests;

/// <summary>Verifies usage replacement, conservative time attribution and deduplicated scoped statistics.</summary>
public sealed class AgentTelemetryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Repeated reads never add cumulative native usage or duplicate observed intervals.</summary>
    [Fact]
    public void PollingReplacesUsageAndCountsEachObservedIntervalOnce()
    {
        var first = AgentTelemetryAccounting.Apply(null, Observation("main", Now, total: 100));
        var next = AgentTelemetryAccounting.Apply(first, Observation("main", Now.AddSeconds(15), total: 130));
        var duplicate = AgentTelemetryAccounting.Apply(next, Observation("main", Now.AddSeconds(15), total: 130));
        Assert.Equal(130, Assert.Single(duplicate.Usage!).TotalTokens);
        Assert.Equal(15_000, Assert.Single(duplicate.ObservedTime).Milliseconds);
        Assert.Equal(next.ObservedTime, duplicate.ObservedTime);
    }

    /// <summary>A model switch is unattributed; a collection gap does not invent activity.</summary>
    [Fact]
    public void ModelSwitchAndGapCannotBeChargedToLatestConfiguration()
    {
        var first = AgentTelemetryAccounting.Apply(null, Observation("main", Now));
        var switched = AgentTelemetryAccounting.Apply(first, Observation("main", Now.AddSeconds(15), "gpt-6-astra", "ultra"));
        var unknown = Assert.Single(switched.ObservedTime);
        Assert.Null(unknown.Model);
        Assert.Null(unknown.ReasoningEffort);
        var afterGap = AgentTelemetryAccounting.Apply(switched, Observation("main", Now.AddMinutes(4), "gpt-6-astra", "ultra"));
        Assert.Equal(switched.ObservedTime, afterGap.ObservedTime);
    }

    /// <summary>Native token groups remain under their historical model instead of the current setting.</summary>
    [Fact]
    public void HistoricalTokensStayUnderSourceModelAndNullIsNotZero()
    {
        var record = AgentTelemetryAccounting.Apply(null, Observation("main", Now) with
        {
            Sample = Sample("main", Now) with
            {
                Usage = [new("gpt-5.6-terra", "low", 0, 0, null, 0)],
            },
        });
        var stats = Project(record);
        var terra = Assert.Single(stats.Groups, group => group.Model == "gpt-5.6-terra");
        Assert.Equal(0, terra.Totals.TotalTokens);
        Assert.Null(terra.Totals.OutputTokens);
        Assert.False(terra.Totals.TokenCoverageComplete);
        var sol = Assert.Single(stats.Groups, group => group.Model == "gpt-6.1-sol");
        Assert.Null(sol.Totals.TotalTokens);
    }

    /// <summary>Parallel agents add agent time, while shared project membership never multiplies global usage.</summary>
    [Fact]
    public void TaskTreesAndSharedProjectsDeduplicateAgentsAndUsage()
    {
        var parent = AgentTelemetryAccounting.Apply(null, Observation("main", Now, total: 100));
        var child = AgentTelemetryAccounting.Apply(null, Observation("child", Now, total: 50) with
        { IsSubagent = true, Sample = Sample("child", Now, total: 50) with { ParentThreadId = "main" } });
        var shared = AgentTelemetryAccounting.Apply(parent, Observation("main", Now.AddSeconds(15), total: 100) with
        { WorkspaceIds = ["two"] });
        var state = new AgentTelemetryState([shared, child, child], null);
        var global = AgentStatistics.Project(state, null, null, new Dictionary<string, string>(), Now.AddSeconds(15));
        Assert.Equal(2, global.Totals.AgentCount);
        Assert.Equal(1, global.Totals.SubagentCount);
        Assert.Equal(1, global.Totals.TaskCount);
        Assert.Equal(150, global.Totals.TotalTokens);
        Assert.Equal(2, global.Projects.Count);
        var project = AgentStatistics.Project(state, "one", null, new Dictionary<string, string>(), Now.AddSeconds(15));
        Assert.Equal(1, project.SharedAgentCount);
        Assert.Equal(50, project.Totals.TotalTokens);
        var task = AgentStatistics.Project(state, "one", AgentDisplayNames.PublicId("main"), new Dictionary<string, string>(), Now.AddSeconds(15));
        Assert.Equal(150, task.Totals.TotalTokens);
        Assert.DoesNotContain("main", task.Tasks.Single().Id, StringComparison.Ordinal);
    }

    /// <summary>A task includes descendants working in other projects without charging their usage to the parent's project.</summary>
    [Fact]
    public void TaskScopeIncludesCrossProjectDescendantsAfterAssociationCheck()
    {
        var main = AgentTelemetryAccounting.Apply(null, Observation("main", Now));
        var child = AgentTelemetryAccounting.Apply(null, Observation("child", Now, total: 50) with
        { WorkspaceIds = ["two"], IsSubagent = true, Sample = Sample("child", Now, total: 50) with { ParentThreadId = "main" } });
        var grandchild = AgentTelemetryAccounting.Apply(null, Observation("grandchild", Now, total: 25) with
        { WorkspaceIds = ["three"], IsSubagent = true, Sample = Sample("grandchild", Now, total: 25) with { ParentThreadId = "child" } });
        var unrelated = AgentTelemetryAccounting.Apply(null, Observation("unrelated", Now, total: 20) with { WorkspaceIds = ["two"] });
        var state = new AgentTelemetryState([main, child, grandchild, unrelated], null);
        AgentStatisticsSnapshot Read(string workspace, string? task = null) =>
            AgentStatistics.Project(state, workspace, task, new Dictionary<string, string>(), Now);
        var task = Read("one", AgentDisplayNames.PublicId("main"));
        Assert.Equal(3, task.Totals.AgentCount);
        Assert.Equal(2, task.Totals.SubagentCount);
        Assert.Equal(175, task.Totals.TotalTokens);
        Assert.Equal(175, Read("two", AgentDisplayNames.PublicId("main")).Totals.TotalTokens);
        var project = Read("one");
        Assert.Equal(100, project.Totals.TotalTokens);
        Assert.Equal(task.Totals, Assert.Single(project.Tasks).Totals);
        Assert.Empty(Read("one", AgentDisplayNames.PublicId("unrelated")).Tasks);
        Assert.Equal(0, Read("one", AgentDisplayNames.PublicId("unrelated")).Totals.AgentCount);
    }

    /// <summary>A failed later source preserves retained counters but clears the current configuration and marks usage stale.</summary>
    [Fact]
    public void FailedReadKeepsRetainedUsageExplicitlyStale()
    {
        var first = AgentTelemetryAccounting.Apply(null, Observation("main", Now));
        var failed = AgentTelemetryAccounting.Apply(first, Observation("main", Now.AddSeconds(15)) with
        { Sample = Sample("main", Now.AddSeconds(15)) with { Configuration = null, Usage = null, CompletedDurationMs = null, Error = "Unavailable" } });
        Assert.Null(failed.Configuration);
        Assert.False(failed.UsageCurrent);
        Assert.Equal(first.Usage, failed.Usage);
        var stats = Project(failed);
        Assert.Equal(1, stats.Totals.StaleUsageAgents);
        Assert.False(stats.Totals.TokenCoverageComplete);
    }

    /// <summary>Concurrent store updates and process restarts preserve lifetime history without multiplying it.</summary>
    [Fact]
    public async Task AtomicStoreRetainsRecordsAndRejectsCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new FileAgentTelemetryStore(root, TimeProvider.System);
            using var second = new FileAgentTelemetryStore(root, TimeProvider.System);
            await Task.WhenAll(first.ObserveAsync([Observation("main", Now)], CancellationToken.None),
                second.ObserveAsync([Observation("main", Now)], CancellationToken.None));
            using var restart = new FileAgentTelemetryStore(root, TimeProvider.System);
            var record = Assert.Single((await restart.ReadAsync(CancellationToken.None)).Records);
            Assert.Equal(100, record.Usage!.Single().TotalTokens);
            Assert.Empty(record.ObservedTime);
            var path = Path.Combine(root, AgentDisplayNames.PublicId("main") + ".json");
            await File.WriteAllTextAsync(path, "invalid");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => restart.ObserveAsync([Observation("main", Now.AddSeconds(15))], CancellationToken.None));
            Assert.Equal("invalid", await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>Cancelling publication while a Windows reader holds the destination preserves the prior complete record.</summary>
    [Fact]
    public async Task CancelledContendedPublicationPreservesPriorRecord()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "cave-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new FileAgentTelemetryStore(root, TimeProvider.System);
            await store.ObserveAsync([Observation("main", Now)], CancellationToken.None);
            var path = Path.Combine(root, AgentDisplayNames.PublicId("main") + ".json");
            var before = await File.ReadAllTextAsync(path);
            await using (var blocking = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    store.ObserveAsync([Observation("main", Now.AddSeconds(15))], stopping.Token));
            }
            Assert.Equal(before, await File.ReadAllTextAsync(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>Independent readers keep complete snapshots without denying an atomic observation update.</summary>
    [Fact]
    public async Task ConcurrentReadersDoNotBlockAtomicObservation()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var writer = new FileAgentTelemetryStore(root, TimeProvider.System);
            await writer.ObserveAsync([Observation("main", Now)], CancellationToken.None);
            async Task ReadContinuously()
            {
                for (var index = 0; index < 100; index++)
                {
                    using var reader = new FileAgentTelemetryStore(root, TimeProvider.System);
                    var read = await reader.ReadAsync(CancellationToken.None);
                    Assert.Null(read.Error);
                    Assert.Equal(100, Assert.Single(read.Records).Usage!.Single().TotalTokens);
                }
            }
            async Task WriteContinuously()
            {
                for (var index = 1; index <= 100; index++)
                    await writer.ObserveAsync([Observation("main", Now.AddSeconds(index))], CancellationToken.None);
            }
            await Task.WhenAll(ReadContinuously(), ReadContinuously(), ReadContinuously(), WriteContinuously());
            Assert.Equal(Now.AddSeconds(100), Assert.Single((await writer.ReadAsync(CancellationToken.None)).Records).LastSampleAtUtc);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>Null elements in a valid JSON envelope cannot crash all statistics or overwrite retained history.</summary>
    [Theory]
    [InlineData("usage")]
    [InlineData("observedTime")]
    public async Task AtomicStoreRejectsNullElements(string collection)
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new FileAgentTelemetryStore(root, TimeProvider.System);
            await store.ObserveAsync([Observation("main", Now)], CancellationToken.None);
            var path = Path.Combine(root, AgentDisplayNames.PublicId("main") + ".json");
            var document = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            document["record"]![collection] = System.Text.Json.Nodes.JsonNode.Parse("[null]");
            var malformed = document.ToJsonString();
            await File.WriteAllTextAsync(path, malformed);
            var read = await store.ReadAsync(CancellationToken.None);
            Assert.Empty(read.Records);
            Assert.Contains("partial coverage", read.Error, StringComparison.Ordinal);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ObserveAsync([Observation("main", Now.AddSeconds(15))], CancellationToken.None));
            Assert.Equal(malformed, await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static AgentStatisticsSnapshot Project(params AgentTelemetryRecord[] records) =>
        AgentStatistics.Project(new(records, null), null, null, new Dictionary<string, string>(), Now.AddSeconds(15));

    private static AgentTelemetryObservation Observation(string id, DateTimeOffset at,
        string model = "gpt-6.1-sol", string effort = "xhigh", long total = 100) =>
        new(Sample(id, at, model, effort, total), ["one"], false, true, at);

    private static AgentTelemetrySample Sample(string id, DateTimeOffset at,
        string model = "gpt-6.1-sol", string effort = "xhigh", long total = 100) =>
        new(id, null, new(model, effort), [new(model, effort, total - 10, 0, 10, total)], 60_000, 1, true, at, null);
}
