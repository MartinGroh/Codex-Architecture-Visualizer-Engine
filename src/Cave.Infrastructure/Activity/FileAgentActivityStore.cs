using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Activity;

/// <summary>
/// Persists normalized activity events as atomic repository-local files and projects them onto the semantic graph.
/// </summary>
/// <param name="timeProvider">The authoritative clock used for declarations and freshness.</param>
public sealed class FileAgentActivityStore(TimeProvider timeProvider) : IAgentActivityStore, IAgentActivityHistoryStore, ICodexTaskLocator
{
    /// <summary>
    /// Gets the workspace-relative directory watched by the live graph monitor.
    /// </summary>
    public const string ActivityDirectory = ".cave/activity/inbox";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private const int MaxProjectedEvents = 2048;
    private const int MaxRecentHistoryEvents = 512;
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecentEditWindow = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public async Task<AgentActivityOverlay> ReadAsync(
        string workspaceRoot,
        ArchitectureGraph graph,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(graph);

        var read = await ReadEventsAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return read.Events.Count == 0 && read.Errors.Count == 0
            ? AgentActivityOverlay.Empty
            : Project(read.Events, graph, timeProvider.GetUtcNow(), read.Errors);
    }

    /// <inheritdoc />
    public async Task<AgentActivityHistoryReadResult> ReadHistoryAsync(
        string workspaceRoot,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (windowStartUtc > windowEndUtc)
        {
            throw new ArgumentException("The activity history window start must not follow its end.");
        }

        var directory = GetActivityDirectory(workspaceRoot);
        if (!Directory.Exists(directory))
        {
            return new AgentActivityHistoryReadResult([], true, false, 0);
        }

        var startPrefix = windowStartUtc.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var endPrefix = windowEndUtc.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var retainedPaths = new SortedSet<string>(StringComparer.Ordinal);
        var candidateOverflow = false;
        var retainedCandidateCount = MaxRecentHistoryEvents + 1;
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(path);
                if (!HasTimestampInWindow(fileName, startPrefix, endPrefix))
                {
                    continue;
                }

                retainedPaths.Add(path);
                if (retainedPaths.Count > retainedCandidateCount)
                {
                    retainedPaths.Remove(retainedPaths.Min!);
                    candidateOverflow = true;
                }
            }
        }
        catch (IOException)
        {
            return AgentActivityHistoryReadResult.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return AgentActivityHistoryReadResult.Unavailable;
        }

        var events = new List<AgentActivityHistoryEvent>(MaxRecentHistoryEvents);
        var invalidRecordCount = 0;
        var isTruncated = candidateOverflow;
        foreach (var path in retainedPaths.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var activityEvent = await JsonSerializer.DeserializeAsync<ActivityEvent>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (activityEvent is null
                    || activityEvent.SchemaVersion != 1
                    || !IsSafeIdentity(activityEvent.EventId, 128)
                    || !IsSafeIdentity(activityEvent.Kind, 64)
                    || activityEvent.OccurredAtUtc == default)
                {
                    invalidRecordCount++;
                    continue;
                }

                var occurredAtUtc = activityEvent.OccurredAtUtc.ToUniversalTime();
                if (occurredAtUtc < windowStartUtc.ToUniversalTime()
                    || occurredAtUtc > windowEndUtc.ToUniversalTime())
                {
                    continue;
                }

                if (Cave.Infrastructure.Conversation.EphemeralConversationSession.IsMarked(
                        workspaceRoot,
                        activityEvent.SessionId))
                {
                    continue;
                }

                var sessionId = SafeOptionalIdentity(activityEvent.SessionId, 128);
                var turnId = SafeOptionalIdentity(activityEvent.TurnId, 128);
                var rawAgentId = SafeOptionalIdentity(activityEvent.AgentId, 128);
                var isSubagent = activityEvent.IsSubagent ?? false;
                var agentId = rawAgentId
                    ?? (sessionId is null ? null : $"session:{sessionId}");
                if (agentId is null || !IsSafeIdentity(agentId, 128))
                {
                    invalidRecordCount++;
                    continue;
                }

                AgentActivityPhase? phase = activityEvent.Phase is { } suppliedPhase
                    && Enum.IsDefined(suppliedPhase)
                    ? suppliedPhase
                    : null;
                events.Add(new AgentActivityHistoryEvent(
                    activityEvent.EventId.Trim(),
                    occurredAtUtc,
                    sessionId,
                    turnId,
                    agentId,
                    SafeOptionalText(activityEvent.AgentType, 64),
                    isSubagent,
                    SafeAction(activityEvent.Kind, activityEvent.ToolName),
                    phase,
                    activityEvent.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase)
                        ? AgentActivityEvidenceKind.Declared
                        : AgentActivityEvidenceKind.Observed));

                if (events.Count > MaxRecentHistoryEvents)
                {
                    events.RemoveAt(events.Count - 1);
                    isTruncated = true;
                    break;
                }
            }
            catch (JsonException)
            {
                invalidRecordCount++;
            }
            catch (IOException)
            {
                if (File.Exists(path))
                {
                    invalidRecordCount++;
                }
            }
            catch (UnauthorizedAccessException)
            {
                invalidRecordCount++;
            }
        }

        return new AgentActivityHistoryReadResult(
            events,
            true,
            isTruncated,
            invalidRecordCount);
    }

    /// <inheritdoc />
    public async Task<CodexTaskBinding?> ResolveAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var read = await ReadEventsAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var mainEvents = read.Events
            .Where(item => item.IsSubagent is not true && !string.IsNullOrWhiteSpace(item.SessionId))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => IsTerminal(item.Kind) ? 1 : 0)
            .ThenBy(item => item.EventId, StringComparer.Ordinal)
            .ToArray();
        var latest = mainEvents.LastOrDefault();
        if (latest is null)
        {
            return null;
        }

        var sessionId = latest.SessionId!.Trim();
        var sessionEvents = mainEvents
            .Where(item => string.Equals(item.SessionId?.Trim(), sessionId, StringComparison.Ordinal))
            .ToArray();
        var latestTurnId = sessionEvents
            .Select(item => item.TurnId?.Trim())
            .LastOrDefault(item => !string.IsNullOrWhiteSpace(item));
        return new CodexTaskBinding(
            sessionId,
            latestTurnId,
            // CONSTRAINT: the five-minute UI liveness lease must not become a task-ownership lease.
            // A quiet Codex turn may legitimately reason longer than that. Only an observed lifecycle
            // completion releases the exact task for a second App Server owner.
            IsActive: !IsTerminal(latest.Kind),
            latest.OccurredAtUtc);
    }

    /// <inheritdoc />
    public async Task AppendScopeAsync(
        string workspaceRoot,
        AgentScopeDeclaration declaration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.AgentType);

        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{canonicalRoot}' does not exist.");
        }

        var occurredAt = timeProvider.GetUtcNow();
        var mainTaskBinding = declaration.IsSubagent
            ? null
            : await ResolveAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var bindToObservedMainTask = mainTaskBinding?.IsActive is true;
        var activityEvent = new ActivityEvent(
            SchemaVersion: 1,
            EventId: Guid.NewGuid().ToString("N"),
            Kind: "ScopeDeclared",
            OccurredAtUtc: occurredAt,
            // CONSTRAINT: a declared scope is additional evidence about the calling Codex task,
            // not a second agent. Bind main-agent declarations to the exact hook-observed task
            // whenever that ownership evidence exists; retain the supplied id as the offline fallback.
            SessionId: bindToObservedMainTask ? mainTaskBinding!.SessionId : null,
            TurnId: bindToObservedMainTask ? mainTaskBinding!.TurnId : null,
            AgentId: bindToObservedMainTask ? null : declaration.AgentId.Trim(),
            AgentType: declaration.AgentType.Trim(),
            IsSubagent: declaration.IsSubagent,
            WorkspaceRoot: canonicalRoot,
            ToolName: null,
            IsMutation: null,
            Phase: declaration.State == AgentWorkState.Active ? AgentActivityPhase.Working : null,
            Paths: NormalizeDistinct(declaration.Files),
            Summary: NullIfWhiteSpace(declaration.Summary),
            State: declaration.State,
            Scope: new ActivityScope(
                NormalizeDistinct(declaration.Projects),
                NormalizeDistinct(declaration.Namespaces),
                NormalizeDistinct(declaration.Classes),
                NormalizeDistinct(declaration.Files)));

        var directory = GetActivityDirectory(canonicalRoot);
        Directory.CreateDirectory(directory);
        var stem = $"{occurredAt:yyyyMMddHHmmssfffffff}-{activityEvent.EventId}";
        var temporaryPath = Path.Combine(directory, $".{stem}.tmp");
        var finalPath = Path.Combine(directory, $"{stem}.json");
        var json = JsonSerializer.Serialize(activityEvent, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, finalPath);
    }

    /// <summary>
    /// Resolves the canonical journal directory for a workspace.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <returns>The absolute activity directory.</returns>
    public static string GetActivityDirectory(string workspaceRoot) =>
        Path.Combine(Path.GetFullPath(workspaceRoot), ".cave", "activity", "inbox");

    private static async Task<ActivityRead> ReadEventsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var directory = GetActivityDirectory(workspaceRoot);
        if (!Directory.Exists(directory))
        {
            return new ActivityRead([], []);
        }

        var events = new List<ActivityEvent>();
        var errors = new List<string>();
        var retainedPaths = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderDescending(StringComparer.Ordinal)
            .Take(MaxProjectedEvents)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var path in retainedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var activityEvent = await JsonSerializer.DeserializeAsync<ActivityEvent>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (activityEvent is null || activityEvent.SchemaVersion != 1)
                {
                    errors.Add($"{Path.GetFileName(path)} has an unsupported activity schema.");
                    continue;
                }

                if (Cave.Infrastructure.Conversation.EphemeralConversationSession.IsMarked(
                        workspaceRoot,
                        activityEvent.SessionId))
                {
                    continue;
                }

                events.Add(activityEvent);
            }
            catch (JsonException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
            catch (IOException exception)
            {
                if (File.Exists(path))
                {
                    errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
                }
            }
        }

        return new ActivityRead(events, errors);
    }

    private static AgentActivityOverlay Project(
        IReadOnlyList<ActivityEvent> events,
        ArchitectureGraph graph,
        DateTimeOffset now,
        List<string> errors)
    {
        var declarationBindings = BuildLegacyMainDeclarationBindings(events, now);
        var agents = new Dictionary<string, AgentAccumulator>(StringComparer.Ordinal);
        foreach (var activityEvent in OrderEvents(events))
        {
            var agentId = ResolveAgentId(activityEvent, declarationBindings);
            if (agentId is null)
            {
                continue;
            }

            if (!agents.TryGetValue(agentId, out var agent))
            {
                agent = new AgentAccumulator(agentId, activityEvent.OccurredAtUtc);
                agents.Add(agentId, agent);
            }

            agent.Apply(activityEvent);
        }

        var projectedAgents = agents.Values
            .Select(agent => agent.Build(now))
            .OrderByDescending(agent => agent.State == AgentWorkState.Active)
            .ThenBy(agent => agent.IsSubagent)
            .ThenByDescending(agent => agent.UpdatedAtUtc)
            .ToArray();
        var agentIds = projectedAgents.Select(agent => agent.AgentId).ToHashSet(StringComparer.Ordinal);

        var nodesByPath = BuildNodesByPath(graph);
        var nodeActivities = new Dictionary<string, AgentNodeAccumulator>(StringComparer.Ordinal);
        var recentEdits = new List<RecentAgentEdit>();
        var unmappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var latestInstructionEvent = events
            .Where(item => item.Kind.Equals("UserPromptSubmit", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.EventId, StringComparer.Ordinal)
            .FirstOrDefault();

        foreach (var activityEvent in OrderEvents(events))
        {
            var agentId = ResolveAgentId(activityEvent, declarationBindings);
            if (agentId is null || !agentIds.Contains(agentId))
            {
                continue;
            }

            var isDeclaration = activityEvent.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase);
            var mappedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (isDeclaration && activityEvent.Scope is not null)
            {
                AddNamedScopeMatches(graph, activityEvent.Scope, mappedNodeIds);
            }

            foreach (var path in activityEvent.Paths.Concat(activityEvent.Scope?.Files ?? []))
            {
                var normalizedPath = NormalizePath(path);
                if (nodesByPath.TryGetValue(normalizedPath, out var matchingNodes))
                {
                    foreach (var nodeId in matchingNodes)
                    {
                        mappedNodeIds.Add(nodeId);
                    }
                }
                else if (FindContainingProject(graph, normalizedPath) is { } projectId)
                {
                    // CONSTRAINT: CodeGraph projections intentionally omit some symbol kinds (for example,
                    // TypeScript functions). Activity in a known project must still light its project card.
                    mappedNodeIds.Add(projectId);
                }
                else
                {
                    unmappedPaths.Add(normalizedPath);
                }
            }

            var evidence = isDeclaration
                ? AgentActivityEvidenceKind.Declared
                : AgentActivityEvidenceKind.Observed;
            foreach (var nodeId in mappedNodeIds)
            {
                var key = $"{agentId}\0{nodeId}\0{evidence}";
                if (!nodeActivities.TryGetValue(key, out var nodeActivity))
                {
                    nodeActivity = new AgentNodeAccumulator(agentId, nodeId, evidence);
                    nodeActivities.Add(key, nodeActivity);
                }

                nodeActivity.Apply(activityEvent);
            }

            if (!isDeclaration
                && activityEvent.Kind.Equals("PostToolUse", StringComparison.OrdinalIgnoreCase)
                && activityEvent.IsMutation is true
                && now - activityEvent.OccurredAtUtc <= RecentEditWindow)
            {
                foreach (var path in activityEvent.Paths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var pathNodeIds = nodesByPath.TryGetValue(path, out var ids) ? ids : [];
                    recentEdits.Add(new RecentAgentEdit(agentId, path, pathNodeIds, activityEvent.OccurredAtUtc));
                }
            }
        }

        return new AgentActivityOverlay(
            projectedAgents,
            nodeActivities.Values.Select(item => item.Build()).ToArray(),
            recentEdits.OrderByDescending(edit => edit.ObservedAtUtc).ToArray(),
            latestInstructionEvent is null
                ? null
                : new AgentInstructionMarker(
                    $"{latestInstructionEvent.SessionId ?? "session"}:{latestInstructionEvent.TurnId ?? latestInstructionEvent.EventId}",
                    latestInstructionEvent.OccurredAtUtc),
            unmappedPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            errors.Count == 0 ? null : string.Join(" ", errors))
        {
            SourceStatus = errors.Count > 0
                ? AgentActivitySourceStatus.Degraded
                : events.Count > 0
                    ? AgentActivitySourceStatus.Ready
                    : AgentActivitySourceStatus.Unobserved,
        };
    }

    private static IOrderedEnumerable<ActivityEvent> OrderEvents(IEnumerable<ActivityEvent> events) =>
        events.OrderBy(item => item.OccurredAtUtc)
            // Lifecycle completion wins timestamp ties so an active tool or declaration cannot
            // resurrect an agent when concurrent hook processes report the same clock tick.
            .ThenBy(item => IsTerminal(item.Kind) ? 1 : 0)
            .ThenBy(item => item.EventId, StringComparer.Ordinal);

    private static bool IsTerminal(string kind) =>
        kind.Equals("Stop", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("Interrupt", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("SessionEnd", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("SubagentStop", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, IReadOnlyList<string>> BuildNodesByPath(ArchitectureGraph graph) =>
        graph.Nodes
            .SelectMany(node => node.SourceLocations.Select(location => (Path: NormalizePath(location.FilePath), node.Id)))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static string? FindContainingProject(ArchitectureGraph graph, string normalizedPath) =>
        graph.Nodes
            .Where(node => node.Kind == ArchitectureNodeKind.Project)
            .Select(node => (node.Id, Root: NormalizePath(node.QualifiedName ?? string.Empty)))
            .Where(project => project.Root.Length > 0
                && (project.Root == "."
                    || normalizedPath.Equals(project.Root, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(project.Root + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(project => project.Root == "." ? 0 : project.Root.Length)
            .Select(project => project.Id)
            .FirstOrDefault();

    private static void AddNamedScopeMatches(
        ArchitectureGraph graph,
        ActivityScope scope,
        ISet<string> matches)
    {
        AddExactMatches(graph.Nodes.Where(node => node.Kind == ArchitectureNodeKind.Project), scope.Projects, matches);
        AddExactMatches(graph.Nodes.Where(node => node.Kind == ArchitectureNodeKind.Namespace), scope.Namespaces, matches);
        AddExactMatches(
            graph.Nodes.Where(node => node.Kind is ArchitectureNodeKind.Class
                or ArchitectureNodeKind.Interface
                or ArchitectureNodeKind.AbstractClass),
            scope.Classes,
            matches);
    }

    private static void AddExactMatches(
        IEnumerable<ArchitectureNode> candidates,
        IReadOnlyList<string> requested,
        ISet<string> matches)
    {
        var names = requested.Select(value => value.Trim()).Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var node in candidates)
        {
            if (names.Contains(node.Id)
                || names.Contains(node.Name)
                || (node.QualifiedName is not null && names.Contains(node.QualifiedName)))
            {
                matches.Add(node.Id);
            }
        }
    }

    private static Dictionary<string, string> BuildLegacyMainDeclarationBindings(
        IReadOnlyList<ActivityEvent> events,
        DateTimeOffset now)
    {
        var activeObservedMainSessions = events
            .Where(item => !item.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase)
                && item.IsSubagent is not true
                && !string.IsNullOrWhiteSpace(item.SessionId))
            .GroupBy(item => item.SessionId!.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = OrderEvents(group).ToArray();
                return new ObservedMainSession(
                    $"session:{group.Key}",
                    ordered[0].OccurredAtUtc,
                    ordered[^1]);
            })
            .Where(session => !IsTerminal(session.Latest.Kind)
                && now - session.Latest.OccurredAtUtc <= ActiveWindow)
            .ToArray();
        if (activeObservedMainSessions.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var declaration in events.Where(item =>
                     item.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase)
                     && item.IsSubagent is not true
                     && string.IsNullOrWhiteSpace(item.SessionId)
                     && !string.IsNullOrWhiteSpace(item.AgentId)
                     && now - item.OccurredAtUtc <= ActiveWindow))
        {
            // Compatibility for declarations written before AppendScopeAsync carried the hook-bound
            // session id. The closest live main session is the only defensible owner of fresh scope
            // evidence; older declarations remain historical agents and are never rewritten on disk.
            var owner = activeObservedMainSessions
                .Where(session => session.StartedAtUtc <= declaration.OccurredAtUtc)
                .OrderBy(session => Math.Abs((session.Latest.OccurredAtUtc - declaration.OccurredAtUtc).Ticks))
                .ThenByDescending(session => session.Latest.OccurredAtUtc)
                .FirstOrDefault();
            if (owner is not null)
            {
                bindings[declaration.EventId] = owner.AgentId;
            }
        }

        return bindings;
    }

    private static string? ResolveAgentId(
        ActivityEvent activityEvent,
        Dictionary<string, string>? declarationBindings = null)
    {
        if (declarationBindings?.TryGetValue(activityEvent.EventId, out var boundAgentId) is true)
        {
            return boundAgentId;
        }

        if (!string.IsNullOrWhiteSpace(activityEvent.AgentId))
        {
            return activityEvent.AgentId.Trim();
        }

        return string.IsNullOrWhiteSpace(activityEvent.SessionId)
            ? null
            : $"session:{activityEvent.SessionId.Trim()}";
    }

    private static string NormalizePath(string value) =>
        value.Trim().Replace('\\', '/').TrimStart('.', '/');

    private static bool HasTimestampInWindow(string fileName, string startPrefix, string endPrefix)
    {
        if (fileName.Length < 21 || fileName.AsSpan(0, 14).ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        // Hook timestamps contain six fractional digits; C# declarations contain seven.
        var fractionalDigits = fileName[20] == '-' ? 6
            : fileName.Length >= 22 && fileName[21] == '-' ? 7
            : 0;
        if (fractionalDigits == 0
            || fileName.AsSpan(14, fractionalDigits).ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        var timestampPrefix = fileName[..14];
        return string.CompareOrdinal(timestampPrefix, startPrefix) >= 0
            && string.CompareOrdinal(timestampPrefix, endPrefix) <= 0;
    }

    private static string SafeAction(string kind, string? toolName)
    {
        if (kind.Equals("SessionStart", StringComparison.OrdinalIgnoreCase))
        {
            return "Session started";
        }

        if (kind.Equals("SessionEnd", StringComparison.OrdinalIgnoreCase))
        {
            return "Session completed";
        }

        if (kind.Equals("UserPromptSubmit", StringComparison.OrdinalIgnoreCase))
        {
            return "Instruction received";
        }

        if (kind.Equals("SubagentStart", StringComparison.OrdinalIgnoreCase))
        {
            return "Subagent started";
        }

        if (kind.Equals("SubagentStop", StringComparison.OrdinalIgnoreCase))
        {
            return "Subagent completed";
        }

        if (kind.Equals("Stop", StringComparison.OrdinalIgnoreCase))
        {
            return "Waiting for the next turn";
        }

        if (kind.Equals("Interrupt", StringComparison.OrdinalIgnoreCase))
        {
            return "Turn interrupted";
        }

        if (kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase))
        {
            return "Scope declared";
        }

        var safeToolName = IsSafeToolName(toolName) ? toolName!.Trim() : null;
        if (kind.Equals("PreToolUse", StringComparison.OrdinalIgnoreCase))
        {
            return safeToolName is null ? "Started a tool" : $"Started {safeToolName}";
        }

        if (kind.Equals("PostToolUse", StringComparison.OrdinalIgnoreCase))
        {
            return safeToolName is null ? "Used a tool" : $"Used {safeToolName}";
        }

        return "Activity observed";
    }

    private static bool IsSafeToolName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' or '.' or ':' or '/');

    private static string? SafeOptionalIdentity(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || !IsSafeIdentity(value, maximumLength)
            ? null
            : value.Trim();

    private static bool IsSafeIdentity(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);

    private static string? SafeOptionalText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('_' or '-' or '.' or ':' or '/' or ' '))
                ? null
                : value.Trim();

    private static string[] NormalizeDistinct(IEnumerable<string>? values) =>
        (values ?? []).Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record ActivityEvent(
        int SchemaVersion,
        string EventId,
        string Kind,
        DateTimeOffset OccurredAtUtc,
        string? SessionId,
        string? TurnId,
        string? AgentId,
        string? AgentType,
        bool? IsSubagent,
        string? WorkspaceRoot,
        string? ToolName,
        bool? IsMutation,
        AgentActivityPhase? Phase,
        IReadOnlyList<string> Paths,
        string? Summary,
        AgentWorkState? State,
        ActivityScope? Scope);

    private sealed record ActivityRead(
        IReadOnlyList<ActivityEvent> Events,
        List<string> Errors);

    private sealed record ActivityScope(
        IReadOnlyList<string> Projects,
        IReadOnlyList<string> Namespaces,
        IReadOnlyList<string> Classes,
        IReadOnlyList<string> Files);

    private sealed record ObservedMainSession(
        string AgentId,
        DateTimeOffset StartedAtUtc,
        ActivityEvent Latest);

    private sealed class AgentAccumulator(string agentId, DateTimeOffset startedAt)
    {
        private ActivityEvent? _latestObserved;
        private ActivityEvent? _latestDeclared;
        private DateTimeOffset _startedAt = startedAt;

        public void Apply(ActivityEvent activityEvent)
        {
            if (activityEvent.OccurredAtUtc < _startedAt)
            {
                _startedAt = activityEvent.OccurredAtUtc;
            }

            if (activityEvent.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase))
            {
                _latestDeclared = activityEvent;
            }
            else
            {
                _latestObserved = activityEvent;
            }
        }

        public AgentActivity Build(DateTimeOffset now)
        {
            var latest = Latest(_latestObserved, _latestDeclared)!;
            // CONSTRAINT: Lifecycle state follows the newest evidence. An older Active scope must not
            // override a later SubagentStop, and an abandoned declaration must not pulse forever.
            var state = latest.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase)
                ? latest.State ?? AgentWorkState.Idle
                : ObservedState(latest);
            if (state == AgentWorkState.Active
                && now - latest.OccurredAtUtc > ActiveWindow)
            {
                state = AgentWorkState.Idle;
            }

            return new AgentActivity(
                agentId,
                latest.AgentType?.Trim() ?? (latest.AgentId is null ? "main" : "subagent"),
                latest.IsSubagent ?? latest.AgentId is not null,
                state,
                state == AgentWorkState.Active ? ResolvePhase(latest) : null,
                NullIfWhiteSpace(_latestDeclared?.Summary) ?? ObservedSummary(_latestObserved),
                _latestObserved is not null,
                _latestDeclared is not null,
                _startedAt,
                latest.OccurredAtUtc)
            {
                Evidence = latest.Kind.Equals("ScopeDeclared", StringComparison.OrdinalIgnoreCase)
                    ? AgentActivityEvidenceKind.Declared
                    : AgentActivityEvidenceKind.Observed,
                SummaryEvidence = NullIfWhiteSpace(_latestDeclared?.Summary) is not null
                    ? AgentActivityEvidenceKind.Declared
                    : _latestObserved is not null ? AgentActivityEvidenceKind.Observed : null,
            };
        }

        private static AgentActivityPhase ResolvePhase(ActivityEvent activityEvent)
        {
            if (activityEvent.Phase is { } phase)
            {
                return phase;
            }

            // Compatibility for retained schema-v1 events written before phase classification existed.
            if (activityEvent.IsMutation is true)
            {
                return AgentActivityPhase.Editing;
            }

            if (activityEvent.Kind.Equals("UserPromptSubmit", StringComparison.OrdinalIgnoreCase)
                || activityEvent.Kind.Equals("SessionStart", StringComparison.OrdinalIgnoreCase)
                || activityEvent.Kind.Equals("SubagentStart", StringComparison.OrdinalIgnoreCase))
            {
                return AgentActivityPhase.Thinking;
            }

            var toolName = activityEvent.ToolName ?? string.Empty;
            if (toolName.Contains("read", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("search", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("find", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("open", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("query", StringComparison.OrdinalIgnoreCase)
                || toolName.Contains("explore", StringComparison.OrdinalIgnoreCase))
            {
                return AgentActivityPhase.Reading;
            }

            return AgentActivityPhase.Working;
        }

        private static AgentWorkState ObservedState(ActivityEvent? activityEvent)
        {
            if (activityEvent is null)
            {
                return AgentWorkState.Idle;
            }

            if (activityEvent.Kind.Equals("SessionEnd", StringComparison.OrdinalIgnoreCase)
                || activityEvent.Kind.Equals("SubagentStop", StringComparison.OrdinalIgnoreCase))
            {
                return AgentWorkState.Completed;
            }

            return activityEvent.Kind.Equals("Stop", StringComparison.OrdinalIgnoreCase)
                || activityEvent.Kind.Equals("Interrupt", StringComparison.OrdinalIgnoreCase)
                ? AgentWorkState.Idle
                : AgentWorkState.Active;
        }

        private static string? ObservedSummary(ActivityEvent? activityEvent)
        {
            if (activityEvent is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(activityEvent.Summary))
            {
                return activityEvent.Summary.Trim();
            }

            if (activityEvent.Paths.Count > 0)
            {
                return $"{activityEvent.ToolName ?? "Tool"}: {activityEvent.Paths[0]}";
            }

            return activityEvent.Kind switch
            {
                "SessionStart" => "Session started",
                "SessionEnd" => "Session completed",
                "SubagentStart" => "Subagent started",
                "SubagentStop" => "Subagent completed",
                "Stop" => "Waiting for the next turn",
                "Interrupt" => "Turn interrupted",
                _ => activityEvent.ToolName is null ? activityEvent.Kind : $"Using {activityEvent.ToolName}",
            };
        }

        private static ActivityEvent? Latest(ActivityEvent? left, ActivityEvent? right) =>
            left is null || (right is not null && right.OccurredAtUtc >= left.OccurredAtUtc) ? right : left;
    }

    private sealed class AgentNodeAccumulator(
        string agentId,
        string nodeId,
        AgentActivityEvidenceKind evidence)
    {
        private DateTimeOffset _updatedAt;
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public void Apply(ActivityEvent activityEvent)
        {
            if (activityEvent.OccurredAtUtc > _updatedAt)
            {
                _updatedAt = activityEvent.OccurredAtUtc;
            }

            foreach (var path in activityEvent.Paths.Concat(activityEvent.Scope?.Files ?? []))
            {
                _paths.Add(NormalizePath(path));
            }
        }

        public AgentNodeActivity Build() => new(
            agentId,
            nodeId,
            evidence,
            IsDirect: true,
            _updatedAt,
            _paths.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
