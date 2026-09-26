using Cave.Application;

namespace Cave.Infrastructure.Live;

/// <summary>
/// Creates live workspace monitors while keeping filesystem details outside host composition.
/// </summary>
/// <param name="snapshots">The application snapshot service.</param>
/// <param name="timeProvider">The authoritative clock.</param>
public sealed class WorkspaceGraphMonitorFactory(
    ArchitectureSnapshotService snapshots,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Creates a monitor for the supplied workspace.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <returns>An unstarted workspace monitor.</returns>
    public WorkspaceGraphMonitor Create(string workspaceRoot) =>
        new(workspaceRoot, snapshots, timeProvider);
}
