namespace Cave.Application;

/// <summary>Contains scoped, deduplicated statistics with explicit token and observed-time coverage.</summary>
/// <param name="Scope">Overall, Project or Task.</param>
/// <param name="TrackingSinceUtc">Earliest retained CAVE observation, or null before collection.</param>
/// <param name="UpdatedAtUtc">Latest successful native observation, or null.</param>
/// <param name="Totals">Unique agents within the selected accounting scope.</param>
/// <param name="Groups">Exact model and reasoning-effort buckets; counts can overlap when an agent used multiple models.</param>
/// <param name="Projects">Exclusive project attribution, plus an explicit shared-project bucket.</param>
/// <param name="Tasks">Main-task trees identified only by opaque keys.</param>
/// <param name="SharedAgentCount">Project-membership records excluded from exclusive project totals.</param>
/// <param name="Error">Safe collection or persistence diagnostic.</param>
public sealed record AgentStatisticsSnapshot(string Scope, DateTimeOffset? TrackingSinceUtc, DateTimeOffset? UpdatedAtUtc,
    AgentStatisticsTotals Totals, IReadOnlyList<AgentStatisticsRow> Groups, IReadOnlyList<AgentStatisticsRow> Projects,
    IReadOnlyList<AgentStatisticsRow> Tasks, int SharedAgentCount, string? Error)
{
    /// <summary>Whether this snapshot contains only explicitly synthetic demo records.</summary>
    public bool IsSynthetic { get; init; }
    /// <summary>The version of the read-only statistics contract.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Unique-agent aggregation by Luna, Terra, Sol, Astra or unknown model family.</summary>
    public IReadOnlyList<AgentStatisticsRow> Families { get; init; } = [];
    /// <summary>Unique-agent aggregation by exact model version across effort levels.</summary>
    public IReadOnlyList<AgentStatisticsRow> Models { get; init; } = [];
    /// <summary>Unique-agent aggregation by effort across all models.</summary>
    public IReadOnlyList<AgentStatisticsRow> Efforts { get; init; } = [];
}

/// <summary>Contains additive agent time and source-estimated token counters, never a productivity score.</summary>
/// <param name="AgentCount">Unique native agent threads.</param>
/// <param name="SubagentCount">Unique observed child threads.</param>
/// <param name="TaskCount">Unique observed task trees.</param>
/// <param name="ObservedMilliseconds">Prospective sampled active agent time.</param>
/// <param name="CompletedMilliseconds">Available native completed-turn duration, kept separate from sampled time.</param>
/// <param name="InputTokens">Sum of reported input tokens, or null when none were reported.</param>
/// <param name="CachedInputTokens">Sum of reported cached input tokens, or null.</param>
/// <param name="OutputTokens">Sum of reported output tokens, or null.</param>
/// <param name="TotalTokens">Sum of reported total tokens, or null.</param>
/// <param name="ReportingAgents">Agents with any source-reported token counter.</param>
/// <param name="TokenCoverageComplete">Whether all selected agents and counters are currently available.</param>
/// <param name="StaleUsageAgents">Agents whose retained usage was unavailable on the latest poll.</param>
public sealed record AgentStatisticsTotals(int AgentCount, int SubagentCount, int TaskCount,
    long ObservedMilliseconds, long? CompletedMilliseconds, long? InputTokens, long? CachedInputTokens,
    long? OutputTokens, long? TotalTokens, int ReportingAgents, bool TokenCoverageComplete, int StaleUsageAgents)
{
    /// <summary>Whether completed-turn duration is complete for every selected agent.</summary>
    public bool DurationCoverageComplete { get; init; }
}

/// <summary>Contains one model/effort, project or task comparison row.</summary>
/// <param name="Id">Opaque stable row identity.</param>
/// <param name="Label">The source model, project display name, or generic task label.</param>
/// <param name="Model">Exact model version for a model row, otherwise null.</param>
/// <param name="ReasoningEffort">Source effort for a model row, otherwise null.</param>
/// <param name="Totals">Row totals using the same accounting policy as the selected scope.</param>
public sealed record AgentStatisticsRow(string Id, string Label, string? Model, string? ReasoningEffort, AgentStatisticsTotals Totals);

/// <summary>Owns deterministic aggregation and ancestry correlation over retained telemetry.</summary>
public static class AgentStatistics
{
    /// <summary>Finds the native main-task ancestor without merging a fork merely because it shares a session.</summary>
    public static string RootId(AgentTelemetryRecord record, IReadOnlyDictionary<string, AgentTelemetryRecord> records)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { record.ThreadId };
        var current = record;
        while (current.ParentThreadId is { } parent)
        {
            if (!seen.Add(parent)) return record.ThreadId;
            if (!records.TryGetValue(parent, out var next)) return parent;
            current = next;
        }
        return current.ThreadId;
    }

    /// <summary>Projects one task, an exclusively attributed project, or all tracked projects without double counting.</summary>
    public static AgentStatisticsSnapshot Project(AgentTelemetryState state, string? workspaceId, string? taskId,
        IReadOnlyDictionary<string, string> workspaceNames, DateTimeOffset now, string? collectionError = null)
    {
        var all = state.Records.DistinctBy(record => record.ThreadId, StringComparer.Ordinal)
            .ToDictionary(record => record.ThreadId, StringComparer.Ordinal);
        var taskTrees = all.Values.GroupBy(record => RootId(record, all), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var memberships = all.Values.Where(record => workspaceId is null || record.WorkspaceIds.Contains(workspaceId, StringComparer.Ordinal)).ToArray();
        if (taskId is not null)
        {
            // CONSTRAINT: workspace membership authorizes the task association, not the tree's
            // accounting boundary. A descendant may do its work in a different registered project.
            var associatedRoot = memberships.Select(record => RootId(record, all)).Distinct(StringComparer.Ordinal)
                .FirstOrDefault(root => AgentDisplayNames.PublicId(root) == taskId);
            memberships = associatedRoot is null ? [] : taskTrees[associatedRoot];
        }
        var sharedCount = workspaceId is null || taskId is not null ? 0 : memberships.Count(record => record.WorkspaceIds.Count > 1);
        var selected = workspaceId is not null && taskId is null
            ? memberships.Where(record => record.WorkspaceIds.Count == 1).ToArray() : memberships;

        var keys = selected.SelectMany(record => (record.Usage ?? []).Select(group => (group.Model, group.ReasoningEffort))
                .Concat(record.ObservedTime.Select(group => (group.Model, group.ReasoningEffort)))
                .Append((record.Configuration?.Model, record.Configuration?.ReasoningEffort)))
            .Distinct().OrderBy(key => key.Item1, StringComparer.Ordinal).ThenBy(key => key.Item2, StringComparer.Ordinal).Take(513).ToArray();
        var omittedGroups = keys.Length > 512;
        keys = keys.Take(512).ToArray();
        var groups = keys.Select(key =>
        {
            var members = selected.Where(record => record.Configuration?.Model == key.Item1 && record.Configuration?.ReasoningEffort == key.Item2
                || record.Usage?.Any(group => group.Model == key.Item1 && group.ReasoningEffort == key.Item2) == true
                || record.ObservedTime.Any(group => group.Model == key.Item1 && group.ReasoningEffort == key.Item2)).ToArray();
            return new AgentStatisticsRow(AgentDisplayNames.PublicId($"{key.Item1}\n{key.Item2}"), key.Item1 ?? "Unknown model",
                key.Item1, key.Item2, Totals(members, all, (model, effort) => model == key.Item1 && effort == key.Item2));
        }).ToArray();
        AgentStatisticsRow[] Axis(Func<string?, string?, string> axis, Func<string, string> label, bool modelAxis) =>
            keys.Select(key => axis(key.Item1, key.Item2)).Distinct(StringComparer.Ordinal).Select(value =>
            {
                bool Matches(string? model, string? effort) => axis(model, effort) == value;
                var members = selected.Where(record => Matches(record.Configuration?.Model, record.Configuration?.ReasoningEffort)
                    || record.Usage?.Any(group => Matches(group.Model, group.ReasoningEffort)) == true
                    || record.ObservedTime.Any(group => Matches(group.Model, group.ReasoningEffort))).ToArray();
                return new AgentStatisticsRow(AgentDisplayNames.PublicId(value), label(value),
                    modelAxis && value != "unknown" ? value : null, modelAxis ? null : value == "unknown" ? null : value,
                    Totals(members, all, Matches));
            }).ToArray();
        var projects = selected.GroupBy(record => record.WorkspaceIds.Count == 1 ? record.WorkspaceIds[0] : "shared", StringComparer.Ordinal)
            .Select(group => new AgentStatisticsRow(group.Key,
                group.Key == "shared" ? "Shared across projects" : workspaceNames.GetValueOrDefault(group.Key) ?? "Retained project",
                null, null, Totals(group.ToArray(), all)))
            .OrderByDescending(row => row.Totals.TotalTokens ?? row.Totals.ObservedMilliseconds).ThenBy(row => row.Label, StringComparer.Ordinal).ToArray();
        var tasks = memberships.GroupBy(record => RootId(record, all), StringComparer.Ordinal)
            .Select(group => new AgentStatisticsRow(AgentDisplayNames.PublicId(group.Key), $"Task · {taskTrees[group.Key].Min(record => record.FirstSeenAtUtc):yyyy-MM-dd HH:mm} UTC",
                null, null, Totals(taskTrees[group.Key], all)))
            .OrderByDescending(row => row.Totals.ObservedMilliseconds).ToArray();
        var updated = selected.Select(record => (DateTimeOffset?)record.LastSampleAtUtc).Max();
        var errors = new[] { state.Error, collectionError,
            omittedGroups ? "Some model breakdown rows exceed the 512-row view limit." : null,
            updated is not null && now - updated > TimeSpan.FromMinutes(2) ? "Statistics collection is stale; retained totals are shown." : null }
            .Where(error => error is not null);
        return new(taskId is not null ? "Task" : workspaceId is not null ? "Project" : "Overall",
            selected.Select(record => (DateTimeOffset?)record.FirstSeenAtUtc).Min(), updated, Totals(selected, all), groups,
            projects, tasks, sharedCount, string.Join(" ", errors) is { Length: > 0 } diagnostic ? diagnostic : null)
        {
            IsSynthetic = state.IsSynthetic,
            Families = Axis((model, _) => ModelFamily(model), value => value == "unknown" ? "Unknown model" : char.ToUpperInvariant(value[0]) + value[1..], true),
            Models = Axis((model, _) => model ?? "unknown", value => value == "unknown" ? "Unknown model" : value, true),
            Efforts = Axis((_, effort) => effort ?? "unknown", value => value == "unknown" ? "Unknown effort" : value, false),
        };
    }

    private static string ModelFamily(string? model) => model?.ToLowerInvariant().Split(['-', '_', ' '])
        .FirstOrDefault(part => part is "luna" or "terra" or "sol" or "astra") ?? "unknown";

    private static AgentStatisticsTotals Totals(AgentTelemetryRecord[] records,
        IReadOnlyDictionary<string, AgentTelemetryRecord> all, Func<string?, string?, bool>? filter = null)
    {
        bool Matches(string? model, string? effort) => filter is null || filter(model, effort);
        var tokens = records.SelectMany(record => record.Usage ?? []).Where(group => Matches(group.Model, group.ReasoningEffort)).ToArray();
        long? Sum(IEnumerable<long?> values)
        {
            var known = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            return known.Length == 0 ? null : known.Sum();
        }
        return new(records.Length, records.Count(record => record.IsSubagent),
            records.Select(record => RootId(record, all)).Distinct(StringComparer.Ordinal).Count(),
            records.SelectMany(record => record.ObservedTime).Where(group => Matches(group.Model, group.ReasoningEffort)).Sum(group => group.Milliseconds),
            filter is null ? Sum(records.Select(record => record.CompletedDurationMs)) : null,
            Sum(tokens.Select(group => group.InputTokens)), Sum(tokens.Select(group => group.CachedInputTokens)),
            Sum(tokens.Select(group => group.OutputTokens)), Sum(tokens.Select(group => group.TotalTokens)),
            records.Count(record => record.Usage?.Any(group => Matches(group.Model, group.ReasoningEffort)
                && (group.InputTokens is not null || group.CachedInputTokens is not null || group.OutputTokens is not null || group.TotalTokens is not null)) == true),
            records.Length > 0 && records.All(record => record.UsageCurrent && record.Usage is { Count: > 0 }
                && record.Usage.Where(group => Matches(group.Model, group.ReasoningEffort)).Any()
                && record.Usage.Where(group => Matches(group.Model, group.ReasoningEffort)).All(group => group.InputTokens.HasValue
                    && group.CachedInputTokens.HasValue && group.OutputTokens.HasValue && group.TotalTokens.HasValue)),
            records.Count(record => record.Usage is not null && !record.UsageCurrent))
        { DurationCoverageComplete = records.Length > 0 && records.All(record => record.CompletedDurationMs is not null && record.DurationComplete) };
    }
}
