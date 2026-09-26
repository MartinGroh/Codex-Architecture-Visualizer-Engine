using System.Threading.Channels;
using System.Diagnostics;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Live;

/// <summary>
/// Watches one workspace and publishes debounced, versioned architecture snapshots.
/// </summary>
public sealed class WorkspaceGraphMonitor : IAsyncDisposable
{
    private const string FullReconciliationMarker = "\0full-reconciliation";
    private static readonly TimeSpan LiveOverlayReconciliationInterval = TimeSpan.FromSeconds(2);
    private static readonly HashSet<string> RelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".ts", ".tsx", ".js", ".jsx", ".json",
        ".py", ".go", ".rs", ".java", ".cpp", ".h", ".hpp", ".razor",
        ".md", ".css", ".html", ".xml", ".props", ".targets", ".ps1", ".sh", ".cmd",
        ".yaml", ".yml", ".toml",
    };

    private static readonly HashSet<string> IgnoredSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codegraph", ".cave", "bin", "obj", "node_modules", "artifacts", "wwwroot", "dist", "server",
    };

    private readonly ArchitectureSnapshotService _snapshots;
    private readonly TimeProvider _timeProvider;
    private readonly FileSystemWatcher _watcher;
    private readonly Channel<string> _changes = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private TaskCompletionSource<long> _nextVersion = NewVersionSignal();
    private Task? _worker;
    private LiveArchitectureSnapshot? _current;

    /// <summary>
    /// Initializes a monitor for an absolute workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="snapshots">The application snapshot service.</param>
    /// <param name="timeProvider">The authoritative clock.</param>
    public WorkspaceGraphMonitor(
        string workspaceRoot,
        ArchitectureSnapshotService snapshots,
        TimeProvider timeProvider)
    {
        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(WorkspaceRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{WorkspaceRoot}' does not exist.");
        }

        _snapshots = snapshots;
        _timeProvider = timeProvider;
        _watcher = new FileSystemWatcher(WorkspaceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            InternalBufferSize = 32 * 1024,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;
    }

    /// <summary>
    /// Gets the canonical workspace root.
    /// </summary>
    public string WorkspaceRoot { get; }

    /// <summary>
    /// Starts the initial projection and filesystem watcher once.
    /// </summary>
    /// <param name="cancellationToken">Signals that startup should stop.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_worker is not null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = true;
            try
            {
                await RefreshAsync(
                    [],
                    activityChanged: false,
                    conversationChanged: false,
                    overlaysOnly: false,
                    throwOnFailure: true,
                    cancellationToken)
                    .ConfigureAwait(false);
                _worker = ProcessChangesAsync(_stopping.Token);
            }
            catch
            {
                _watcher.EnableRaisingEvents = false;
                throw;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <summary>
    /// Gets the latest snapshot, starting the monitor when necessary.
    /// </summary>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>The latest versioned snapshot.</returns>
    public async Task<LiveArchitectureSnapshot> GetCurrentAsync(CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        await RefreshLiveOverlaysIfChangedAsync(cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
        {
            return _current!;
        }
    }

    /// <summary>
    /// Waits until a newer snapshot exists or the timeout elapses.
    /// </summary>
    /// <param name="afterVersion">The last version observed by the caller.</param>
    /// <param name="timeout">The maximum wait duration.</param>
    /// <param name="cancellationToken">Signals that the wait should stop.</param>
    /// <returns>The latest snapshot, which may have the same version after a timeout.</returns>
    public async Task<LiveArchitectureSnapshot> WaitForUpdateAsync(
        long afterVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (current.Version > afterVersion)
        {
            return current;
        }

        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            Task versionSignal;
            lock (_stateGate)
            {
                if (_current!.Version > afterVersion)
                {
                    return _current;
                }

                versionSignal = _nextVersion.Task;
            }

            var remaining = timeout - elapsed.Elapsed;
            var reconciliationDelay = remaining < LiveOverlayReconciliationInterval
                ? remaining
                : LiveOverlayReconciliationInterval;
            try
            {
                await versionSignal.WaitAsync(reconciliationDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await RefreshLiveOverlaysIfChangedAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        lock (_stateGate)
        {
            return _current!;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _stopping.Cancel();
        _changes.Writer.TryComplete();

        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _watcher.Dispose();
        _stopping.Dispose();
        _startGate.Dispose();
        _refreshGate.Dispose();
    }

    private static TaskCompletionSource<long> NewVersionSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnChanged(object sender, FileSystemEventArgs eventArgs) => QueuePath(eventArgs.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        QueuePath(eventArgs.OldFullPath);
        QueuePath(eventArgs.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs) =>
        _changes.Writer.TryWrite(FullReconciliationMarker);

    private void QueuePath(string fullPath)
    {
        var relativePath = Path.GetRelativePath(WorkspaceRoot, fullPath).Replace('\\', '/');
        if (IsActivityEventPath(relativePath)
            || IsConversationPath(relativePath)
            || IsGitIdentityPath(relativePath))
        {
            _changes.Writer.TryWrite(relativePath);
            return;
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(IgnoredSegments.Contains)
            || !RelevantExtensions.Contains(Path.GetExtension(relativePath)))
        {
            return;
        }

        _changes.Writer.TryWrite(relativePath);
    }

    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        while (await _changes.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var pendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (_changes.Reader.TryRead(out var pendingPath))
            {
                pendingPaths.Add(pendingPath);
            }

            var overlaysOnlyPending = pendingPaths.Count > 0
                && pendingPaths.All(path => IsActivityEventPath(path) || IsConversationPath(path));
            var debounce = overlaysOnlyPending
                ? TimeSpan.FromMilliseconds(120)
                : TimeSpan.FromMilliseconds(450);
            await Task.Delay(debounce, cancellationToken).ConfigureAwait(false);
            while (_changes.Reader.TryRead(out var delayedPath))
            {
                pendingPaths.Add(delayedPath);
            }

            // CONSTRAINT: The channel is the ownership boundary for pending changes. Keeping a second
            // collection and clearing it after draining the channel can erase an event queued in between.
            var forceFullReconciliation = pendingPaths.Remove(FullReconciliationMarker);
            var changedPaths = pendingPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var activityChanged = changedPaths.Any(IsActivityEventPath);
            var conversationChanged = changedPaths.Any(IsConversationPath);
            var gitIdentityChanged = changedPaths.Any(IsGitIdentityPath);
            var publishedPaths = changedPaths.Where(path => !IsGitIdentityPath(path)).ToArray();
            var sourcePaths = publishedPaths
                .Where(path => !IsActivityEventPath(path) && !IsConversationPath(path))
                .ToArray();
            await RefreshAsync(
                publishedPaths,
                activityChanged,
                conversationChanged,
                overlaysOnly: !forceFullReconciliation
                    && (activityChanged || conversationChanged)
                    && sourcePaths.Length == 0
                    && !gitIdentityChanged,
                throwOnFailure: false,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync(
        IReadOnlyList<string> changedPaths,
        bool activityChanged,
        bool conversationChanged,
        bool overlaysOnly,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Cave.Domain.ArchitectureSnapshot snapshot;
            if (overlaysOnly)
            {
                Cave.Domain.ArchitectureSnapshot current;
                lock (_stateGate)
                {
                    current = _current!.Snapshot;
                }

                snapshot = await _snapshots.RefreshLiveOverlaysAsync(WorkspaceRoot, current, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                snapshot = await _snapshots.GetAsync(WorkspaceRoot, cancellationToken).ConfigureAwait(false);
            }

            Publish(snapshot, changedPaths, activityChanged, conversationChanged, refreshError: null);
        }
        catch (Exception exception) when (!throwOnFailure && exception is not OperationCanceledException)
        {
            lock (_stateGate)
            {
                PublishCore(
                    _current!.Snapshot,
                    changedPaths,
                    activityChanged,
                    conversationChanged,
                    exception.Message);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshLiveOverlaysIfChangedAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LiveArchitectureSnapshot current;
            lock (_stateGate)
            {
                current = _current!;
            }

            var snapshot = await _snapshots.RefreshLiveOverlaysAsync(
                WorkspaceRoot,
                current.Snapshot,
                cancellationToken).ConfigureAwait(false);
            var activityChanged = !ActivityEquals(current.Snapshot.Activity, snapshot.Activity);
            var conversationChanged = !ConversationEquals(
                current.Snapshot.Conversation,
                snapshot.Conversation);
            if (!activityChanged && !conversationChanged)
            {
                return;
            }

            lock (_stateGate)
            {
                // CONSTRAINT: A concurrent filesystem refresh owns the newer semantic snapshot.
                // Never overwrite it with live overlays projected against the older graph.
                if (_current!.Version != current.Version)
                {
                    return;
                }

                PublishCore(
                    snapshot,
                    [],
                    activityChanged,
                    conversationChanged,
                    refreshError: current.RefreshError);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static bool ActivityEquals(AgentActivityOverlay left, AgentActivityOverlay right) =>
        left.Agents.SequenceEqual(right.Agents)
        && SequenceEqual(left.Nodes, right.Nodes, NodeActivityEquals)
        && SequenceEqual(left.RecentEdits, right.RecentEdits, RecentEditEquals)
        && Equals(left.LatestInstruction, right.LatestInstruction)
        && left.UnmappedPaths.SequenceEqual(right.UnmappedPaths, StringComparer.OrdinalIgnoreCase)
        && string.Equals(left.Error, right.Error, StringComparison.Ordinal);

    private static bool ConversationEquals(ConversationOverlay left, ConversationOverlay right) =>
        left.SharingEnabled == right.SharingEnabled
        && left.Status == right.Status
        && left.Messages.SequenceEqual(right.Messages)
        && string.Equals(left.Error, right.Error, StringComparison.Ordinal);

    private static bool NodeActivityEquals(AgentNodeActivity left, AgentNodeActivity right) =>
        left.AgentId == right.AgentId
        && left.NodeId == right.NodeId
        && left.Evidence == right.Evidence
        && left.IsDirect == right.IsDirect
        && left.UpdatedAtUtc == right.UpdatedAtUtc
        && left.Paths.SequenceEqual(right.Paths, StringComparer.OrdinalIgnoreCase);

    private static bool RecentEditEquals(RecentAgentEdit left, RecentAgentEdit right) =>
        left.AgentId == right.AgentId
        && left.FilePath.Equals(right.FilePath, StringComparison.OrdinalIgnoreCase)
        && left.ObservedAtUtc == right.ObservedAtUtc
        && left.NodeIds.SequenceEqual(right.NodeIds, StringComparer.Ordinal);

    private static bool SequenceEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool> equals) =>
        left.Count == right.Count && left.Zip(right).All(pair => equals(pair.First, pair.Second));

    private void Publish(
        Cave.Domain.ArchitectureSnapshot snapshot,
        IReadOnlyList<string> changedPaths,
        bool activityChanged,
        bool conversationChanged,
        string? refreshError)
    {
        lock (_stateGate)
        {
            PublishCore(snapshot, changedPaths, activityChanged, conversationChanged, refreshError);
        }
    }

    private void PublishCore(
        Cave.Domain.ArchitectureSnapshot snapshot,
        IReadOnlyList<string> changedPaths,
        bool activityChanged,
        bool conversationChanged,
        string? refreshError)
    {
        var version = (_current?.Version ?? 0) + 1;
        _current = new LiveArchitectureSnapshot(
            version,
            snapshot,
            changedPaths,
            activityChanged,
            conversationChanged,
            _timeProvider.GetUtcNow(),
            refreshError);
        var completedSignal = _nextVersion;
        _nextVersion = NewVersionSignal();
        completedSignal.TrySetResult(version);
    }

    private static bool IsActivityEventPath(string relativePath) =>
        relativePath.StartsWith(".cave/activity/inbox/", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(relativePath).Equals(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsConversationPath(string relativePath) =>
        relativePath.Equals(".cave/conversation/settings.json", StringComparison.OrdinalIgnoreCase)
        || (relativePath.StartsWith(".cave/conversation/inbox/", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(relativePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        || (relativePath.StartsWith(".cave/conversation/control/", StringComparison.OrdinalIgnoreCase)
            && Path.GetExtension(relativePath).Equals(".json", StringComparison.OrdinalIgnoreCase));

    private static bool IsGitIdentityPath(string relativePath) =>
        relativePath.Equals(".git/HEAD", StringComparison.OrdinalIgnoreCase)
        || relativePath.Equals(".git/packed-refs", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith(".git/refs/heads/", StringComparison.OrdinalIgnoreCase);
}
