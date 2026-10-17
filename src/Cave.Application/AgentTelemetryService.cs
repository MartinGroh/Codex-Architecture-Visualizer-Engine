using Cave.Domain;

namespace Cave.Application;

/// <summary>Owns collection, identity correlation and presentation enrichment without changing activity liveness.</summary>
/// <param name="store">The canonical retained telemetry store.</param>
/// <param name="source">The configured native or explicit demo telemetry source.</param>
/// <param name="activityStore">The canonical observed activity reducer.</param>
/// <param name="timeProvider">The authoritative clock.</param>
public sealed class AgentTelemetryService(IAgentTelemetryStore store, IAgentTelemetrySource source,
    IAgentActivityStore activityStore, TimeProvider timeProvider)
{
    private static readonly ArchitectureGraph EmptyGraph = ArchitectureGraph.Create([], []);
    private int _cursor;
    private int _activeCursor;
    private string? _collectionError;

    /// <summary>Collects one bounded pass over exact agents already observed in registered workspaces.</summary>
    public async Task CollectAsync(IReadOnlyList<WorkspaceCatalogEntry> registrations, CancellationToken cancellationToken)
    {
        var targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        foreach (var workspace in registrations)
        {
            var activity = await activityStore.ReadAsync(workspace.WorkspaceRoot, EmptyGraph, cancellationToken).ConfigureAwait(false);
            foreach (var agent in activity.Agents.Where(agent => agent.HasObservedActivity))
            {
                var id = NativeId(agent.AgentId, agent.IsSubagent);
                if (id is null) continue;
                if (!targets.TryGetValue(id, out var target)) targets[id] = target = new(agent);
                target.WorkspaceIds.Add(workspace.WorkspaceId);
                if (agent.UpdatedAtUtc > target.Agent.UpdatedAtUtc) target.Agent = agent;
            }
        }
        // Continue retaining finished identities across activity-journal pruning. Old tasks rotate
        // through a small background budget; active observed agents always get first priority.
        var retained = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in retained.Records)
        {
            if (!targets.ContainsKey(record.ThreadId))
                targets[record.ThreadId] = new(new(record.ThreadId, "retained", record.IsSubagent, AgentWorkState.Completed,
                    null, null, true, false, record.FirstSeenAtUtc, record.ActivityAtUtc));
            targets[record.ThreadId].WorkspaceIds.UnionWith(record.WorkspaceIds);
        }
        var running = targets.Where(pair => pair.Value.Agent.State == AgentWorkState.Active)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var inactive = targets.Where(pair => pair.Value.Agent.State != AgentWorkState.Active).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var activeCount = Math.Min(inactive.Length == 0 ? 32 : 24, running.Length);
        var active = Enumerable.Range(0, activeCount).Select(offset => running[(_activeCursor + offset) % running.Length]).ToArray();
        if (running.Length > 0) _activeCursor = (_activeCursor + activeCount) % running.Length;
        var count = Math.Min(32 - active.Length, inactive.Length);
        var selected = active.Concat(Enumerable.Range(0, count)
            .Select(offset => inactive[(_cursor + offset) % inactive.Length])).ToArray();
        if (inactive.Length > 0) _cursor = (_cursor + count) % inactive.Length;
        if (selected.Length == 0) return;
        try
        {
            var samples = await source.ReadAsync(selected.Select(pair => pair.Key).ToArray(), cancellationToken).ConfigureAwait(false);
            var observations = samples.Where(sample => targets.ContainsKey(sample.ThreadId)).Select(sample =>
            {
                var target = targets[sample.ThreadId];
                return new AgentTelemetryObservation(sample, target.WorkspaceIds.Order(StringComparer.Ordinal).ToArray(),
                    target.Agent.IsSubagent, target.Agent.State == AgentWorkState.Active, target.Agent.UpdatedAtUtc);
            }).ToArray();
            await store.ObserveAsync(observations, cancellationToken).ConfigureAwait(false);
            _collectionError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { _collectionError = "Statistics collection could not update its retained records."; }
    }

    /// <summary>Attaches exact configured metadata and presentation names; never inherits another agent's model.</summary>
    public async Task<AgentActivityOverlay> EnrichAsync(AgentActivityOverlay activity, CancellationToken cancellationToken)
    {
        var records = (await store.ReadAsync(cancellationToken).ConfigureAwait(false)).Records
            .ToDictionary(record => record.ThreadId, StringComparer.Ordinal);
        var named = AgentDisplayNames.Apply(activity);
        return named with
        {
            Agents = named.Agents.Select(agent =>
            {
                var id = NativeId(agent.AgentId, agent.IsSubagent);
                var configuration = id is not null && records.TryGetValue(id, out var record)
                    && timeProvider.GetUtcNow() >= record.LastSampleAtUtc
                    && timeProvider.GetUtcNow() - record.LastSampleAtUtc <= TimeSpan.FromMinutes(2)
                    ? record.Configuration : null;
                return agent with { Configuration = configuration,
                    StatisticsTaskId = id is not null && records.TryGetValue(id, out var known)
                        ? AgentDisplayNames.PublicId(AgentStatistics.RootId(known, records)) : null };
            }).ToArray(),
        };
    }

    /// <summary>Reads scoped statistics without issuing native requests on an HTTP or graph refresh path.</summary>
    public async Task<AgentStatisticsSnapshot> ReadStatisticsAsync(string? workspaceId, string? taskId,
        IReadOnlyDictionary<string, string> workspaceNames, CancellationToken cancellationToken)
    {
        var state = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return AgentStatistics.Project(state, workspaceId, taskId, workspaceNames, timeProvider.GetUtcNow(), _collectionError);
    }

    private static string? NativeId(string id, bool child)
    {
        if (id.StartsWith("scope:", StringComparison.Ordinal)) return null;
        var native = !child && id.StartsWith("session:", StringComparison.Ordinal) ? id[8..] : id;
        return AgentTelemetryIdentity.IsNative(native) ? native : null;
    }

    private sealed class Target(AgentActivity agent)
    {
        internal AgentActivity Agent = agent;
        internal HashSet<string> WorkspaceIds { get; } = new(StringComparer.Ordinal);
    }
}
