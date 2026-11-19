using Cave.Domain;

namespace Cave.Application;

/// <summary>Projects shared, published conversation for one observed task tree and authorizes its inline artifacts.</summary>
/// <param name="activity">The canonical workspace activity reader used to bind tasks.</param>
/// <param name="conversations">The public conversation journal and sharing authority.</param>
/// <param name="source">The exact-task native chat source.</param>
/// <param name="clock">The clock for activity and cache lifetimes.</param>
/// <param name="sourceMode">Whether the composition uses real or synthetic sources.</param>
public sealed class WorkspaceAgentChatService(
    WorkspaceActivityService activity,
    IConversationStore conversations,
    ICodexAgentChatSource source,
    TimeProvider clock,
    AgentFlowSourceMode sourceMode)
{
    private const int MaximumEntries = 512;
    private const int MaximumCachedTasks = 6;
    private const int MaximumImageBytesPerTask = 8 * 1024 * 1024;
    private static readonly TimeSpan RecentActivity = TimeSpan.FromMinutes(10);
    private readonly Dictionary<ScopeKey, TaskRead> _reads = [];

    /// <summary>Returns a bounded snapshot for the exact observed root, or null for an unknown task.</summary>
    public async Task<AgentChatSnapshot?> ReadAsync(WorkspaceCatalogEntry workspace, string taskId, CancellationToken cancellationToken)
    {
        var scope = await ResolveAsync(workspace, taskId, cancellationToken).ConfigureAwait(false);
        if (scope is null) return null;
        var sharing = await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!sharing.Enabled) return Empty(AgentChatSharing.Private);
        var period = sharing.AgentChatPeriod;
        var observed = scope.Agents.ToDictionary(agent => ThreadId(agent.AgentId), StringComparer.Ordinal);
        var observedParents = observed.ToDictionary(pair => pair.Key, pair => ThreadId(pair.Value.ParentAgentId), StringComparer.Ordinal);
        var observedMembers = Descendants(scope.ThreadId, observedParents);
        var journal = await conversations.ReadAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);

        if (period is null)
        {
            var current = await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
            if (current != sharing || !journal.SharingEnabled) return Empty(AgentChatSharing.Private);
            return Project(scope, observed, observedParents, observedMembers, journal, null, null, out _);
        }

        var key = new ScopeKey(workspace.WorkspaceId, scope.ThreadId, period.Id);
        var read = GetRead(key, acquire: true);
        if (read is null)
        {
            var current = await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
            return current == sharing && journal.SharingEnabled
                ? Project(scope, observed, observedParents, observedMembers, journal, null, period, out _)
                : Empty(AgentChatSharing.Private);
        }

        var entered = false;
        try
        {
            await read.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            if (await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false) != sharing)
            {
                return Empty(AgentChatSharing.Private);
            }

            var native = read.Batch;
            if (native is null || clock.GetUtcNow() - read.ObservedAtUtc >= TimeSpan.FromSeconds(2))
            {
                native = await source.ReadAsync(scope.ThreadId, observedMembers, period.StartedAtUtc, cancellationToken)
                    .ConfigureAwait(false);
                if (!StringComparer.Ordinal.Equals(native.RootThreadId, scope.ThreadId))
                {
                    native = new(scope.ThreadId, CodexAgentChatStatus.Unavailable, []);
                }
                if (await ResolveAsync(workspace, taskId, cancellationToken).ConfigureAwait(false) is not { } currentScope
                    || currentScope.ThreadId != scope.ThreadId)
                {
                    DropRead(key);
                    return null;
                }
            }

            if (!journal.SharingEnabled
                || await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false) != sharing)
            {
                DropRead(key);
                return Empty(AgentChatSharing.Private);
            }

            if (!ReferenceEquals(native, read.Batch))
            {
                read.Batch = native;
                read.ObservedAtUtc = clock.GetUtcNow();
            }
            var snapshot = Project(scope, observed, observedParents, observedMembers, journal, native, period, out var artifacts);
            lock (_reads)
            {
                var previous = read.Artifacts;
                foreach (var item in previous)
                {
                    artifacts.TryAdd(item.Key, item.Value);
                }
                BoundArtifacts(artifacts);
                read.Artifacts = artifacts;
            }
            return snapshot;
        }
        finally
        {
            if (entered) read.Gate.Release();
            lock (_reads) read.Readers--;
        }
    }

    /// <summary>Reads a previously projected inline image while its task and sharing period are still authorized.</summary>
    public async Task<AgentChatArtifactPayload?> ReadArtifactAsync(
        WorkspaceCatalogEntry workspace, string taskId, string artifactId, CancellationToken cancellationToken)
    {
        var sharing = await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!sharing.Enabled || sharing.AgentChatPeriod is not { } period) return null;
        var scope = await ResolveAsync(workspace, taskId, cancellationToken).ConfigureAwait(false);
        if (scope is null) return null;
        var read = GetRead(new ScopeKey(workspace.WorkspaceId, scope.ThreadId, period.Id), acquire: false);
        AgentChatArtifactPayload? artifact;
        lock (_reads)
        {
            artifact = read?.Artifacts.GetValueOrDefault(artifactId);
        }
        return artifact is not null
            && await conversations.ReadSharingAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false) == sharing
            ? artifact : null;
    }

    private async Task<TaskScope?> ResolveAsync(WorkspaceCatalogEntry workspace, string taskId, CancellationToken cancellationToken)
    {
        var evidence = await activity.ReadEvidenceAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var cutoff = clock.GetUtcNow() - RecentActivity;
        var agents = evidence.Agents.Where(agent => agent.HasObservedActivity
            && AgentTelemetryIdentity.IsNative(ThreadId(agent.AgentId)))
            .GroupBy(agent => ThreadId(agent.AgentId), StringComparer.Ordinal)
            .Select(group => group.OrderBy(agent => agent.IsSubagent).ThenByDescending(agent => agent.UpdatedAtUtc).First())
            .ToArray();
        var root = agents.FirstOrDefault(agent => !agent.IsSubagent
            && (agent.State == AgentWorkState.Active || agent.UpdatedAtUtc >= cutoff)
            && StringComparer.Ordinal.Equals(AgentDisplayNames.PublicId(agent.AgentId), taskId));
        return root is null ? null : new(ThreadId(root.AgentId), agents);
    }

    private AgentChatSnapshot Project(
        TaskScope scope, Dictionary<string, AgentActivity> observed, Dictionary<string, string> observedParents,
        HashSet<string> observedMembers,
        ConversationOverlay journal, CodexAgentChatBatch? native, AgentChatSharingPeriod? period,
        out Dictionary<string, AgentChatArtifactPayload> artifacts)
    {
        artifacts = new(StringComparer.Ordinal);
        var threads = (native?.Threads ?? []).ToDictionary(thread => thread.ThreadId, StringComparer.Ordinal);
        var verified = Descendants(scope.ThreadId,
            threads.ToDictionary(pair => pair.Key, pair => pair.Value.ParentThreadId ?? "", StringComparer.Ordinal));
        var members = observedMembers.Concat(verified).Distinct(StringComparer.Ordinal).ToArray();

        string PublicId(string threadId) =>
            AgentDisplayNames.PublicId(observed.GetValueOrDefault(threadId)?.AgentId ?? threadId);

        var messages = new List<(string ThreadId, AgentChatMessage Message)>();
        foreach (var threadId in members)
        {
            var entries = verified.Contains(threadId) && period is not null
                ? threads.GetValueOrDefault(threadId)?.Entries.Where(entry => entry.PublishedAtUtc >= period.StartedAtUtc).ToArray() ?? []
                : [];
            var nativeBoundaries = entries
                .Where(entry => entry.Kind is ConversationMessageKind.Prompt or ConversationMessageKind.Final)
                .Select(entry => (entry.TurnId, entry.Kind)).ToHashSet();
            var nativeItems = entries.Select(entry => $"appserver:assistant:{entry.ItemId}").ToHashSet(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var id = AgentDisplayNames.PublicId($"{threadId}\0{entry.TurnId}\0{entry.ItemId}");
                var images = new List<AgentChatArtifact>();
                foreach (var image in entry.Images)
                {
                    var artifactId = AgentDisplayNames.PublicId($"{period!.Id:N}:{id}:{images.Count}");
                    var kind = image.ContentType;
                    if (kind is ("image/png" or "image/jpeg" or "image/webp")
                        && image.Bytes is { Length: > 0 and <= MaximumImageBytesPerTask } bytes)
                    {
                        artifacts[artifactId] = new(kind, bytes);
                    }
                    else
                    {
                        kind = null;
                    }
                    images.Add(new(artifactId, kind));
                }
                messages.Add((threadId, new(id, entry.Role, entry.Kind, entry.PublishedAtUtc, entry.Text, entry.IsTruncated, images)));
            }

            foreach (var item in journal.Messages)
            {
                var matches = item.IsSubagent
                    ? threadId == item.AgentId && item.SessionId == scope.ThreadId
                    : threadId == scope.ThreadId && item.SessionId == scope.ThreadId;
                if (!matches || nativeItems.Contains(item.EventId) || (period is not null && item.OccurredAtUtc < period.StartedAtUtc)
                    || nativeBoundaries.Contains((item.TurnId ?? "", item.Kind))) continue;
                messages.Add((threadId, new(AgentDisplayNames.PublicId(item.EventId), item.Role, item.Kind,
                    item.OccurredAtUtc, item.Text, item.IsTruncated, [])));
            }
        }

        var retained = messages.OrderByDescending(pair => pair.Message.OccurredAtUtc)
            .ThenByDescending(pair => pair.Message.Id, StringComparer.Ordinal)
            .Take(MaximumEntries).Reverse().ToLookup(pair => pair.ThreadId, pair => pair.Message, StringComparer.Ordinal);
        var retainedImageIds = retained.SelectMany(lane => lane)
            .SelectMany(message => message.Artifacts).Select(artifact => artifact.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var imageId in artifacts.Keys.Where(id => !retainedImageIds.Contains(id)).ToArray())
            artifacts.Remove(imageId);
        BoundArtifacts(artifacts);
        var availableArtifacts = artifacts.Keys.ToHashSet(StringComparer.Ordinal);
        var agents = members.OrderByDescending(id => id == scope.ThreadId)
            .ThenBy(id => observed.GetValueOrDefault(id)?.StartedAtUtc)
            .ThenBy(id => id, StringComparer.Ordinal)
            .Select(id =>
            {
                var row = observed.GetValueOrDefault(id);
                var thread = verified.Contains(id) ? threads.GetValueOrDefault(id) : null;
                var parent = id == scope.ThreadId ? null : thread?.ParentThreadId ?? observedParents.GetValueOrDefault(id);
                var publications = retained[id].Select(message => message with
                {
                    Artifacts = message.Artifacts.Select(artifact => availableArtifacts.Contains(artifact.Id)
                        ? artifact : artifact with { ContentType = null }).ToArray()
                }).ToArray();
                return new AgentChatLane(PublicId(id), parent is not null ? PublicId(parent) : null,
                    row?.DisplayName ?? AgentDisplayNames.Get(row?.AgentId ?? id, id != scope.ThreadId),
                    row?.StartedAtUtc, thread?.Status ?? CodexAgentChatStatus.Unavailable, publications);
            }).ToArray();
        return new(period is null ? AgentChatSharing.Legacy : AgentChatSharing.Shared, period?.Id.ToString("N"),
            sourceMode, native?.Status ?? CodexAgentChatStatus.Unavailable, agents);
    }

    private AgentChatSnapshot Empty(AgentChatSharing sharing) =>
        new(sharing, null, sourceMode, CodexAgentChatStatus.Unavailable, []);

    private static HashSet<string> Descendants(string root, Dictionary<string, string> parents)
    {
        var members = new HashSet<string>(StringComparer.Ordinal) { root };
        while (true)
        {
            var size = members.Count;
            foreach (var (id, parent) in parents)
            {
                if (members.Contains(parent)) members.Add(id);
            }
            if (members.Count == size) return members;
        }
    }

    private static string ThreadId(string? id) =>
        id?.StartsWith("scope:", StringComparison.Ordinal) == true ? ""
        : id?.StartsWith("session:", StringComparison.Ordinal) == true ? id[8..] : id ?? "";

    private TaskRead? GetRead(ScopeKey key, bool acquire)
    {
        lock (_reads)
        {
            var now = clock.GetUtcNow();
            foreach (var stale in _reads.Where(pair => pair.Value.Readers == 0
                && now - pair.Value.LastAccess > TimeSpan.FromMinutes(2)).Select(pair => pair.Key).ToArray())
                _reads.Remove(stale);
            if (!_reads.TryGetValue(key, out var read))
            {
                if (!acquire) return null;
                if (_reads.Count >= MaximumCachedTasks)
                {
                    var oldest = _reads.Where(pair => pair.Value.Readers == 0)
                        .OrderBy(pair => pair.Value.LastAccess).FirstOrDefault();
                    if (oldest.Key is null) return null;
                    _reads.Remove(oldest.Key);
                }
                read = new TaskRead();
                _reads.Add(key, read);
            }
            read.LastAccess = now;
            if (acquire) read.Readers++;
            return read;
        }
    }

    private void DropRead(ScopeKey key)
    {
        lock (_reads) _reads.Remove(key);
    }

    private static void BoundArtifacts(Dictionary<string, AgentChatArtifactPayload> artifacts)
    {
        var remaining = MaximumImageBytesPerTask;
        foreach (var artifact in artifacts.ToArray())
        {
            if (artifact.Value.Bytes.Length <= remaining) remaining -= artifact.Value.Bytes.Length;
            else artifacts.Remove(artifact.Key);
        }
    }

    private sealed record TaskScope(string ThreadId, AgentActivity[] Agents);
    private sealed record ScopeKey(string WorkspaceId, string ThreadId, Guid PeriodId);

    private sealed class TaskRead
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int Readers { get; set; }
        public DateTimeOffset LastAccess { get; set; }
        public DateTimeOffset ObservedAtUtc { get; set; }
        public CodexAgentChatBatch? Batch { get; set; }
        public Dictionary<string, AgentChatArtifactPayload> Artifacts { get; set; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Describes the workspace's current permission to show agent chat.</summary>
public enum AgentChatSharing
{
    /// <summary>Chat is private.</summary>
    Private,
    /// <summary>Only existing, opt-in hook messages are shared.</summary>
    Legacy,
    /// <summary>Published native history is shared for the current period.</summary>
    Shared,
}

/// <summary>A bounded conversation snapshot for one observed task tree.</summary>
/// <param name="Sharing">The current workspace sharing decision.</param>
/// <param name="PeriodId">The revocable sharing-period identity, when native history is enabled.</param>
/// <param name="SourceMode">Whether the view uses live or explicit synthetic evidence.</param>
/// <param name="Status">Availability of published history for this batch.</param>
/// <param name="Agents">Correlated agent lanes with published messages.</param>
public sealed record AgentChatSnapshot(AgentChatSharing Sharing, string? PeriodId, AgentFlowSourceMode SourceMode,
    CodexAgentChatStatus Status, IReadOnlyList<AgentChatLane> Agents);

/// <summary>Published chat and identity for an agent belonging to the selected task.</summary>
/// <param name="Id">The opaque agent correlation key.</param>
/// <param name="ParentId">The opaque immediate parent key, when known.</param>
/// <param name="DisplayName">The stable presentation label.</param>
/// <param name="StartedAtUtc">The first observed lifecycle time, when available.</param>
/// <param name="Status">Availability of the agent's published history.</param>
/// <param name="Messages">Public messages ordered by publication time.</param>
public sealed record AgentChatLane(string Id, string? ParentId, string DisplayName, DateTimeOffset? StartedAtUtc,
    CodexAgentChatStatus Status, IReadOnlyList<AgentChatMessage> Messages);

/// <summary>A single published conversation entry and its inline artifact references.</summary>
/// <param name="Id">The opaque stable item identity.</param>
/// <param name="Role">The public author role.</param>
/// <param name="Kind">The source's public message phase.</param>
/// <param name="OccurredAtUtc">The publication time.</param>
/// <param name="Text">The bounded public Markdown text.</param>
/// <param name="IsTruncated">Whether the source bounded the text.</param>
/// <param name="Artifacts">Attached raster references.</param>
public sealed record AgentChatMessage(string Id, ConversationRole Role, ConversationMessageKind Kind,
    DateTimeOffset OccurredAtUtc, string Text, bool IsTruncated, IReadOnlyList<AgentChatArtifact> Artifacts);

/// <summary>References a shared image without publishing its path or bytes in the chat feed.</summary>
/// <param name="Id">The opaque image identity bound to the current sharing period.</param>
/// <param name="ContentType">The validated raster media type, or null for an unavailable image.</param>
public sealed record AgentChatArtifact(string Id, string? ContentType);

/// <summary>Represents authorized inline raster media returned by the image endpoint.</summary>
/// <param name="ContentType">The validated raster media type.</param>
/// <param name="Bytes">The bounded inline image payload.</param>
public sealed record AgentChatArtifactPayload(string ContentType, byte[] Bytes);
