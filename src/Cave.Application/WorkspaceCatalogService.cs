using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Builds the browser-facing machine workspace overview from catalog and activity evidence.
/// </summary>
/// <param name="catalogStore">The machine-local workspace catalog.</param>
/// <param name="activityStore">The repository-local agent activity source.</param>
public sealed class WorkspaceCatalogService(
    IWorkspaceCatalogStore catalogStore,
    IAgentActivityStore activityStore)
{
    private static readonly ArchitectureGraph EmptyGraph = ArchitectureGraph.Create([], []);

    /// <summary>
    /// Lists initialized or unavailable registered workspaces with current activity summaries.
    /// </summary>
    /// <param name="cancellationToken">Signals that overview construction should stop.</param>
    /// <returns>The current workspace overview and any non-fatal diagnostics.</returns>
    public async Task<WorkspaceOverviewSnapshot> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var catalog = await catalogStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var overviewTasks = catalog.Entries
            .Where(IsProjectWorkspace)
            .Select(entry => CreateOverviewAsync(entry, cancellationToken));
        var overviews = await Task.WhenAll(overviewTasks).ConfigureAwait(false);
        var errors = catalog.Errors
            .Concat(overviews
                .Where(item => item.ActivityError is not null)
                .Select(item => $"{item.Name}: {item.ActivityError}"))
            .ToArray();

        return new WorkspaceOverviewSnapshot(
            Environment.MachineName,
            overviews
                .OrderByDescending(item => item.ActiveAgentCount > 0)
                .ThenByDescending(item => item.LastActivityAtUtc)
                .ThenByDescending(item => item.LastSeenAtUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors);
    }

    /// <summary>
    /// Resolves one opaque workspace identifier to its visible machine-local project entry.
    /// </summary>
    /// <param name="workspaceId">The opaque workspace identifier.</param>
    /// <param name="cancellationToken">Signals that the lookup should stop.</param>
    /// <returns>The matching entry, or <see langword="null"/> when it is not a visible project.</returns>
    public async Task<WorkspaceCatalogEntry?> FindAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        var catalog = await catalogStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        return catalog.Entries.FirstOrDefault(
            item => item.WorkspaceId.Equals(workspaceId, StringComparison.Ordinal)
                && IsProjectWorkspace(item));
    }

    private static bool IsProjectWorkspace(WorkspaceCatalogEntry entry)
    {
        if (!Directory.Exists(entry.WorkspaceRoot))
        {
            // Keep deleted/moved initialized registrations visible so the operator can diagnose them.
            return true;
        }

        return File.Exists(Path.Combine(entry.WorkspaceRoot, ".codegraph", "codegraph.db"));
    }

    private async Task<WorkspaceOverview> CreateOverviewAsync(
        WorkspaceCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        var isAvailable = Directory.Exists(entry.WorkspaceRoot);
        var activity = isAvailable
            ? await activityStore.ReadAsync(entry.WorkspaceRoot, EmptyGraph, cancellationToken)
                .ConfigureAwait(false)
            : AgentActivityOverlay.Empty;
        var activeAgents = activity.Agents
            .Where(agent => agent.State == AgentWorkState.Active)
            .OrderBy(agent => agent.IsSubagent)
            .ThenByDescending(agent => agent.UpdatedAtUtc)
            .ToArray();
        var latestAgent = activity.Agents
            .OrderByDescending(agent => agent.UpdatedAtUtc)
            .FirstOrDefault();
        var currentAgent = activeAgents.FirstOrDefault();

        return new WorkspaceOverview(
            entry.WorkspaceId,
            new DirectoryInfo(entry.WorkspaceRoot).Name,
            entry.WorkspaceRoot,
            isAvailable,
            entry.LastSeenAtUtc,
            activity.Agents.Count,
            activeAgents.Length,
            activeAgents.Count(agent => agent.IsSubagent),
            currentAgent?.Phase,
            currentAgent?.Summary ?? latestAgent?.Summary,
            latestAgent?.UpdatedAtUtc,
            activity.Error);
    }
}

/// <summary>
/// Summarizes one registered workspace for the machine dashboard.
/// </summary>
/// <param name="WorkspaceId">The stable opaque workspace identifier.</param>
/// <param name="Name">The display name derived from the workspace directory.</param>
/// <param name="RootPath">The canonical absolute workspace path.</param>
/// <param name="IsAvailable">Whether the workspace directory currently exists.</param>
/// <param name="LastSeenAtUtc">The latest time CAVE observed the workspace.</param>
/// <param name="KnownAgentCount">The number of retained agent identities.</param>
/// <param name="ActiveAgentCount">The number of agents with fresh active evidence.</param>
/// <param name="ActiveSubagentCount">The active agents identified as subagents.</param>
/// <param name="CurrentPhase">The phase of the preferred active agent.</param>
/// <param name="ActivitySummary">The latest privacy-minimized activity summary.</param>
/// <param name="LastActivityAtUtc">The latest retained agent activity time.</param>
/// <param name="ActivityError">A repository activity read error, or <see langword="null"/>.</param>
public sealed record WorkspaceOverview(
    string WorkspaceId,
    string Name,
    string RootPath,
    bool IsAvailable,
    DateTimeOffset LastSeenAtUtc,
    int KnownAgentCount,
    int ActiveAgentCount,
    int ActiveSubagentCount,
    AgentActivityPhase? CurrentPhase,
    string? ActivitySummary,
    DateTimeOffset? LastActivityAtUtc,
    string? ActivityError);

/// <summary>
/// Carries the complete machine dashboard state.
/// </summary>
/// <param name="HostName">The operating-system name of the machine serving this dashboard.</param>
/// <param name="Workspaces">Registered workspace summaries.</param>
/// <param name="Errors">Visible catalog and activity diagnostics.</param>
public sealed record WorkspaceOverviewSnapshot(
    string HostName,
    IReadOnlyList<WorkspaceOverview> Workspaces,
    IReadOnlyList<string> Errors);
