using System.Collections.Concurrent;
using Cave.Application;

namespace Cave.Infrastructure.Live;

/// <summary>
/// Owns one live graph monitor per canonical workspace for the lifetime of a composition root.
/// </summary>
/// <param name="monitorFactory">The live monitor factory.</param>
/// <param name="catalogStore">The machine-local workspace catalog refreshed when a workspace is opened.</param>
public sealed class WorkspaceGraphMonitorRegistry(
    WorkspaceGraphMonitorFactory monitorFactory,
    IWorkspaceCatalogStore catalogStore) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<WorkspaceGraphSession>>> _byRoot =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WorkspaceGraphSession> _bySubscription =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or creates the single live session for an absolute workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="cancellationToken">Signals that registration or session startup should stop.</param>
    /// <returns>The live workspace session.</returns>
    public async Task<WorkspaceGraphSession> GetOrCreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException("CAVE requires an absolute workspace path.", nameof(workspaceRoot));
        }

        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        await catalogStore.RegisterAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var sessionTask = _byRoot.GetOrAdd(
            canonicalRoot,
            root => new Lazy<Task<WorkspaceGraphSession>>(
                () => CreateSessionAsync(root, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await sessionTask.Value.ConfigureAwait(false);
        }
        catch
        {
            _byRoot.TryRemove(new KeyValuePair<string, Lazy<Task<WorkspaceGraphSession>>>(
                canonicalRoot,
                sessionTask));
            throw;
        }
    }

    /// <summary>
    /// Gets a previously created workspace session by MCP subscription identifier.
    /// </summary>
    /// <param name="subscriptionId">The opaque subscription identifier.</param>
    /// <returns>The matching session.</returns>
    /// <exception cref="KeyNotFoundException">No matching session exists.</exception>
    public WorkspaceGraphSession GetBySubscription(string subscriptionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        return _bySubscription.TryGetValue(subscriptionId, out var session)
            ? session
            : throw new KeyNotFoundException("The CAVE live subscription is no longer active.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var lazySession in _byRoot.Values.Where(item => item.IsValueCreated))
        {
            try
            {
                var session = await lazySession.Value.ConfigureAwait(false);
                await session.Monitor.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session startup already reported cancellation to its caller; there is no monitor to dispose.
            }
        }
    }

    private async Task<WorkspaceGraphSession> CreateSessionAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var monitor = monitorFactory.Create(workspaceRoot);
        try
        {
            await monitor.StartAsync(cancellationToken).ConfigureAwait(false);
            var session = new WorkspaceGraphSession(Guid.NewGuid().ToString("N"), monitor);
            _bySubscription[session.SubscriptionId] = session;
            return session;
        }
        catch
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>
/// Associates an opaque MCP subscription with one owned live workspace monitor.
/// </summary>
/// <param name="SubscriptionId">The opaque subscription identifier.</param>
/// <param name="Monitor">The owned live workspace monitor.</param>
public sealed record WorkspaceGraphSession(
    string SubscriptionId,
    WorkspaceGraphMonitor Monitor);
