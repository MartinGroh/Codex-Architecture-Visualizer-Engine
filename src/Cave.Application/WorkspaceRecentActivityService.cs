namespace Cave.Application;

/// <summary>
/// Produces a bounded, newest-first activity timeline across registered CAVE workspaces.
/// </summary>
/// <param name="catalogStore">The machine catalog that owns workspace identity and membership.</param>
/// <param name="activityHistoryStore">The canonical retained activity event source.</param>
/// <param name="timeProvider">The clock used to define the inclusive recent-activity window.</param>
public sealed class WorkspaceRecentActivityService(
    IWorkspaceCatalogStore catalogStore,
    IAgentActivityHistoryStore activityHistoryStore,
    TimeProvider timeProvider)
{
    private const int MaximumWorkspacesPerRead = 128;
    private const int MaximumPageSize = 200;

    private static readonly int[] SupportedWindows = [1, 5, 10, 15, 30, 60];

    /// <summary>
    /// Reads one global or workspace-scoped recent activity page using an exact supported time window.
    /// </summary>
    /// <param name="minutes">The inclusive lookback length: 1, 5, 10, 15, 30, or 60 minutes.</param>
    /// <param name="workspaceId">An opaque registered workspace id, or <see langword="null"/> for all workspaces.</param>
    /// <param name="offset">The zero-based event offset into the newest-first result.</param>
    /// <param name="pageSize">The maximum number of events to return, from 1 through 200.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The bounded versioned timeline page and its source coverage.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The window, offset, or page size is outside its supported bounds.</exception>
    public async Task<WorkspaceRecentActivityFeed> ReadAsync(
        int minutes,
        string? workspaceId,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!SupportedWindows.Contains(minutes))
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), "Choose 1, 5, 10, 15, 30, or 60 minutes.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The event offset must not be negative.");
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"The page size must be from 1 through {MaximumPageSize}.");
        }

        var generatedAtUtc = timeProvider.GetUtcNow();
        var windowStartUtc = generatedAtUtc.AddMinutes(-minutes);
        var catalog = await catalogStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var allEntries = catalog.Entries
            .OrderBy(entry => entry.WorkspaceId, StringComparer.Ordinal)
            .ToArray();

        WorkspaceCatalogEntry[] selectedEntries;
        string? selectedWorkspaceId = null;
        string? selectedWorkspaceName = null;
        var isWorkspaceScoped = !string.IsNullOrWhiteSpace(workspaceId);
        if (isWorkspaceScoped)
        {
            var entry = allEntries.FirstOrDefault(candidate =>
                candidate.WorkspaceId.Equals(workspaceId, StringComparison.Ordinal));
            if (entry is null)
            {
                return WorkspaceRecentActivityFeed.WorkspaceNotFound(
                    minutes,
                    generatedAtUtc,
                    windowStartUtc,
                    workspaceId!.Trim());
            }

            selectedEntries = [entry];
            selectedWorkspaceId = entry.WorkspaceId;
            selectedWorkspaceName = GetWorkspaceName(entry.WorkspaceRoot);
        }
        else
        {
            selectedEntries = allEntries.Take(MaximumWorkspacesPerRead).ToArray();
        }

        var registeredWorkspaces = selectedEntries
            .Select(entry => new RecentActivityWorkspaceRegistration(
                entry.WorkspaceId,
                GetWorkspaceName(entry.WorkspaceRoot),
                entry.WorkspaceRoot,
                Directory.Exists(entry.WorkspaceRoot)))
            .ToArray();
        var errors = new Dictionary<(string WorkspaceId, string Code), RecentActivitySourceError>();
        var eventMemberships = new Dictionary<string, EventMembership>(StringComparer.Ordinal);
        var queriedWorkspaceCount = 0;
        var readableWorkspaceCount = 0;
        var failedWorkspaceCount = 0;
        var truncatedWorkspaceCount = 0;
        var invalidActivityRecordCount = 0;

        foreach (var entry in selectedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = GetWorkspaceName(entry.WorkspaceRoot);
            if (!Directory.Exists(entry.WorkspaceRoot))
            {
                failedWorkspaceCount++;
                AddError(errors, entry.WorkspaceId, name, "WorkspaceUnavailable");
                continue;
            }

            queriedWorkspaceCount++;
            var read = await activityHistoryStore.ReadHistoryAsync(
                entry.WorkspaceRoot,
                windowStartUtc,
                generatedAtUtc,
                cancellationToken).ConfigureAwait(false);
            if (!read.IsAvailable)
            {
                failedWorkspaceCount++;
                AddError(errors, entry.WorkspaceId, name, "ActivityHistoryUnavailable");
                continue;
            }

            readableWorkspaceCount++;
            if (read.IsTruncated)
            {
                truncatedWorkspaceCount++;
                AddError(errors, entry.WorkspaceId, name, "ActivityHistoryTruncated");
            }

            if (read.InvalidRecordCount > 0)
            {
                invalidActivityRecordCount += read.InvalidRecordCount;
                AddError(errors, entry.WorkspaceId, name, "ActivityHistoryDegraded");
            }

            foreach (var activityEvent in read.Events)
            {
                if (!eventMemberships.TryGetValue(activityEvent.EventId, out var membership))
                {
                    membership = new EventMembership(activityEvent);
                    eventMemberships.Add(activityEvent.EventId, membership);
                }
                else if (membership.Event != activityEvent)
                {
                    membership.HasConflictingCopies = true;
                }

                membership.Workspaces.Add(new RecentActivityWorkspaceMembership(
                    entry.WorkspaceId,
                    name));
            }
        }

        var conflictingCopyCount = eventMemberships.Values.Count(membership => membership.HasConflictingCopies);
        if (conflictingCopyCount > 0)
        {
            AddError(errors, string.Empty, string.Empty, "DuplicateEventConflict");
        }

        var orderedEvents = eventMemberships.Values
            .Select(membership => new RecentActivityEventView(
                membership.Event.EventId,
                membership.Event.OccurredAtUtc,
                membership.Workspaces
                    .OrderBy(item => item.WorkspaceId, StringComparer.Ordinal)
                    .ToArray(),
                membership.Event.SessionId,
                membership.Event.TurnId,
                membership.Event.AgentId,
                membership.Event.AgentType,
                membership.Event.IsSubagent,
                membership.Event.Action,
                membership.Event.Phase?.ToString(),
                membership.Event.Evidence.ToString().ToLowerInvariant()))
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenBy(item => item.EventId, StringComparer.Ordinal)
            .ToArray();

        var page = orderedEvents.Skip(offset).Take(pageSize).ToArray();
        var hasMore = (long)offset + page.Length < orderedEvents.Length;
        var unscannedWorkspaceCount = isWorkspaceScoped
            ? 0
            : Math.Max(0, allEntries.Length - selectedEntries.Length);
        var partial = catalog.Errors.Count > 0
            || failedWorkspaceCount > 0
            || truncatedWorkspaceCount > 0
            || invalidActivityRecordCount > 0
            || conflictingCopyCount > 0
            || unscannedWorkspaceCount > 0;
        var unavailable = selectedEntries.Length > 0 && readableWorkspaceCount == 0;

        return new WorkspaceRecentActivityFeed(
            SchemaVersion: 1,
            Minutes: minutes,
            GeneratedAtUtc: generatedAtUtc,
            WindowStartUtc: windowStartUtc,
            WindowEndUtc: generatedAtUtc,
            Scope: new RecentActivityScope(
                isWorkspaceScoped ? "workspace" : "all",
                selectedWorkspaceId,
                selectedWorkspaceName),
            Coverage: new RecentActivityCoverage(
                unavailable ? "unavailable" : partial ? "partial" : "complete",
                allEntries.Length,
                queriedWorkspaceCount,
                readableWorkspaceCount,
                failedWorkspaceCount,
                truncatedWorkspaceCount,
                invalidActivityRecordCount,
                catalog.Errors.Count,
                unscannedWorkspaceCount,
                conflictingCopyCount,
                errors.Values.OrderBy(error => error.WorkspaceId, StringComparer.Ordinal)
                    .ThenBy(error => error.Code, StringComparer.Ordinal)
                    .ToArray()),
            RegisteredWorkspaceCount: registeredWorkspaces.Length,
            RegisteredWorkspaces: registeredWorkspaces,
            TotalEventCount: orderedEvents.Length,
            OmittedEventCount: orderedEvents.Length - page.Length,
            Offset: offset,
            PageSize: pageSize,
            HasMore: hasMore,
            NextOffset: hasMore ? offset + page.Length : null,
            Events: page);
    }

    private static string GetWorkspaceName(string workspaceRoot) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(workspaceRoot));

    private static void AddError(
        IDictionary<(string WorkspaceId, string Code), RecentActivitySourceError> errors,
        string workspaceId,
        string workspaceName,
        string code)
    {
        var key = (workspaceId, code);
        errors.TryAdd(key, new RecentActivitySourceError(
            string.IsNullOrEmpty(workspaceId) ? null : workspaceId,
            string.IsNullOrEmpty(workspaceName) ? null : workspaceName,
            code));
    }

    private sealed class EventMembership(AgentActivityHistoryEvent activityEvent)
    {
        public AgentActivityHistoryEvent Event { get; } = activityEvent;

        public HashSet<RecentActivityWorkspaceMembership> Workspaces { get; } = [];

        public bool HasConflictingCopies { get; set; }
    }
}

/// <summary>
/// Describes whether a recent activity response covers every selected activity source.
/// </summary>
/// <param name="Status">The complete, partial, or unavailable coverage state.</param>
/// <param name="RegisteredWorkspaceCount">The valid workspace registrations in the machine catalog.</param>
/// <param name="QueriedWorkspaceCount">The workspace histories read during this request.</param>
/// <param name="ReadableWorkspaceCount">The workspace histories that returned a usable result.</param>
/// <param name="FailedWorkspaceCount">The registered workspace roots that could not be read.</param>
/// <param name="TruncatedWorkspaceCount">The workspace histories that reached their bounded event limit.</param>
/// <param name="InvalidActivityRecordCount">The malformed or unreadable records in queried histories.</param>
/// <param name="CatalogErrorCount">The invalid or unreadable machine catalog records.</param>
/// <param name="UnscannedWorkspaceCount">The registered workspaces beyond the bounded global scan.</param>
/// <param name="DuplicateEventConflictCount">Event identities whose copies contained conflicting safe metadata.</param>
/// <param name="Errors">Sanitized source errors without filesystem paths or exception text.</param>
public sealed record RecentActivityCoverage(
    string Status,
    int RegisteredWorkspaceCount,
    int QueriedWorkspaceCount,
    int ReadableWorkspaceCount,
    int FailedWorkspaceCount,
    int TruncatedWorkspaceCount,
    int InvalidActivityRecordCount,
    int CatalogErrorCount,
    int UnscannedWorkspaceCount,
    int DuplicateEventConflictCount,
    IReadOnlyList<RecentActivitySourceError> Errors);

/// <summary>
/// Carries a safe, versioned recent activity page and the workspace catalog used to scope it.
/// </summary>
/// <param name="SchemaVersion">The stable wire schema version.</param>
/// <param name="Minutes">The exact supported lookback window.</param>
/// <param name="GeneratedAtUtc">The upper inclusive boundary of the read.</param>
/// <param name="WindowStartUtc">The lower inclusive boundary of the read.</param>
/// <param name="WindowEndUtc">The upper inclusive boundary of the read.</param>
/// <param name="Scope">Whether the feed covers all registered roots or one exact root.</param>
/// <param name="Coverage">Completeness and bounded-source diagnostics.</param>
/// <param name="RegisteredWorkspaceCount">The number of registrations returned for this scope.</param>
/// <param name="RegisteredWorkspaces">The workspace identities and canonical roots available for task binding.</param>
/// <param name="TotalEventCount">The number of distinct event identities collected before paging.</param>
/// <param name="OmittedEventCount">The collected events omitted from this page, including events before and after its offset.</param>
/// <param name="Offset">The zero-based page offset.</param>
/// <param name="PageSize">The requested bounded page size.</param>
/// <param name="HasMore">Whether another page of collected events remains.</param>
/// <param name="NextOffset">The next page offset, or <see langword="null"/> when this is the last page.</param>
/// <param name="Events">The newest-first safe activity events for this page.</param>
public sealed record WorkspaceRecentActivityFeed(
    int SchemaVersion,
    int Minutes,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    RecentActivityScope Scope,
    RecentActivityCoverage Coverage,
    int RegisteredWorkspaceCount,
    IReadOnlyList<RecentActivityWorkspaceRegistration> RegisteredWorkspaces,
    int TotalEventCount,
    int OmittedEventCount,
    int Offset,
    int PageSize,
    bool HasMore,
    int? NextOffset,
    IReadOnlyList<RecentActivityEventView> Events)
{
    /// <summary>
    /// Creates a not-found result used by the host to return an HTTP 404 for an unknown opaque id.
    /// </summary>
    /// <param name="minutes">The selected window.</param>
    /// <param name="generatedAtUtc">The query end time.</param>
    /// <param name="windowStartUtc">The query start time.</param>
    /// <param name="workspaceId">The requested opaque workspace id.</param>
    /// <returns>An empty feed whose workspace scope identifies the missing id.</returns>
    public static WorkspaceRecentActivityFeed WorkspaceNotFound(
        int minutes,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset windowStartUtc,
        string workspaceId) => new(
        1,
        minutes,
        generatedAtUtc,
        windowStartUtc,
        generatedAtUtc,
        new RecentActivityScope("workspace", workspaceId, null),
        new RecentActivityCoverage("unavailable", 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [new RecentActivitySourceError(workspaceId, null, "WorkspaceNotRegistered")]),
        0,
        [],
        0,
        0,
        0,
        0,
        false,
        null,
        []);
}

/// <summary>
/// Identifies whether a recent activity feed is global or scoped to one registered workspace.
/// </summary>
/// <param name="Kind">The scope name: <c>all</c> or <c>workspace</c>.</param>
/// <param name="WorkspaceId">The selected opaque workspace id, when scoped.</param>
/// <param name="WorkspaceName">The selected workspace name, when scoped.</param>
public sealed record RecentActivityScope(string Kind, string? WorkspaceId, string? WorkspaceName);

/// <summary>
/// Describes a workspace registration included with a recent activity response.
/// </summary>
/// <param name="WorkspaceId">The opaque catalog identity.</param>
/// <param name="WorkspaceName">The final path segment used as the display name.</param>
/// <param name="RootPath">The canonical registered root used for task-to-workspace binding.</param>
/// <param name="Available">Whether the registered directory currently exists.</param>
public sealed record RecentActivityWorkspaceRegistration(
    string WorkspaceId,
    string WorkspaceName,
    string RootPath,
    bool Available);

/// <summary>
/// Associates one event with every registered root that retained a copy of it.
/// </summary>
/// <param name="WorkspaceId">The opaque workspace identity.</param>
/// <param name="WorkspaceName">The workspace display name.</param>
public sealed record RecentActivityWorkspaceMembership(string WorkspaceId, string WorkspaceName);

/// <summary>
/// Exposes a single safe activity event in the HTTP feed.
/// </summary>
/// <param name="EventId">The globally deduplicated journal identity.</param>
/// <param name="OccurredAtUtc">The observed event time.</param>
/// <param name="Memberships">Every workspace journal containing this event.</param>
/// <param name="SessionId">The Codex session identity, when known.</param>
/// <param name="TurnId">The Codex turn identity, when known.</param>
/// <param name="AgentId">The stable session or subagent identity.</param>
/// <param name="AgentType">The Codex agent role, when known.</param>
/// <param name="IsSubagent">Whether the event belongs to a child agent.</param>
/// <param name="Action">The generated safe activity summary.</param>
/// <param name="Phase">The safe phase label, when known.</param>
/// <param name="Evidence">The <c>Observed</c> or <c>Declared</c> evidence source.</param>
public sealed record RecentActivityEventView(
    string EventId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<RecentActivityWorkspaceMembership> Memberships,
    string? SessionId,
    string? TurnId,
    string AgentId,
    string? AgentType,
    bool IsSubagent,
    string Action,
    string? Phase,
    string Evidence);

/// <summary>
/// Reports a sanitized problem observed while reading one registered activity source.
/// </summary>
/// <param name="WorkspaceId">The workspace identity, or null for a catalog-wide issue.</param>
/// <param name="WorkspaceName">The workspace name, or null for a catalog-wide issue.</param>
/// <param name="Code">A stable error code without local paths or exception data.</param>
public sealed record RecentActivitySourceError(string? WorkspaceId, string? WorkspaceName, string Code);
