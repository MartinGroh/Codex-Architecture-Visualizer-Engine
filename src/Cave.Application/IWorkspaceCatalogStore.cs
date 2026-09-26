namespace Cave.Application;

/// <summary>
/// Persists the machine-local set of workspaces known to CAVE.
/// </summary>
public interface IWorkspaceCatalogStore
{
    /// <summary>
    /// Registers or refreshes an absolute workspace root.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="cancellationToken">Signals that the write should stop.</param>
    /// <returns>The canonical catalog entry.</returns>
    Task<WorkspaceCatalogEntry> RegisterAsync(
        string workspaceRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads every valid machine-local catalog entry and reports invalid records separately.
    /// </summary>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current catalog records and any read diagnostics.</returns>
    Task<WorkspaceCatalogReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Identifies one workspace registered on the current machine.
/// </summary>
/// <param name="WorkspaceId">The stable opaque identifier derived from the canonical path.</param>
/// <param name="WorkspaceRoot">The canonical absolute workspace path.</param>
/// <param name="LastSeenAtUtc">The latest time a hook or CAVE composition root observed the workspace.</param>
public sealed record WorkspaceCatalogEntry(
    string WorkspaceId,
    string WorkspaceRoot,
    DateTimeOffset LastSeenAtUtc);

/// <summary>
/// Carries valid workspace records without hiding catalog diagnostics.
/// </summary>
/// <param name="Entries">The valid machine-local entries.</param>
/// <param name="Errors">Diagnostics for invalid or unreadable catalog records.</param>
public sealed record WorkspaceCatalogReadResult(
    IReadOnlyList<WorkspaceCatalogEntry> Entries,
    IReadOnlyList<string> Errors);
