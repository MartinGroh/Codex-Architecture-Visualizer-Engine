namespace Cave.Domain;

/// <summary>
/// Identifies the configured Git comparison baseline.
/// </summary>
public enum GitBaselineKind
{
    /// <summary>Compare with the tracked upstream branch.</summary>
    Upstream,

    /// <summary>Compare with the current HEAD commit.</summary>
    Head,

    /// <summary>Compare with an explicitly configured commit.</summary>
    Commit,
}

/// <summary>
/// Describes whether Git delta evidence is available for a snapshot.
/// </summary>
public enum GitDeltaStatus
{
    /// <summary>Git evidence was read and projected successfully.</summary>
    Ready,

    /// <summary>Git evidence could not be read without substituting another baseline.</summary>
    Unavailable,
}

/// <summary>
/// Classifies one changed file relative to the configured baseline.
/// </summary>
public enum GitFileChangeKind
{
    /// <summary>The file was added to the tracked worktree.</summary>
    Added,

    /// <summary>The tracked file was modified.</summary>
    Modified,

    /// <summary>The tracked file was deleted.</summary>
    Deleted,

    /// <summary>The tracked file was renamed.</summary>
    Renamed,

    /// <summary>The file is present but not tracked by Git.</summary>
    Untracked,
}

/// <summary>
/// Classifies the aggregate structural state of an architecture node.
/// </summary>
public enum GitStructuralChangeKind
{
    /// <summary>All contributing changes add new content.</summary>
    Added,

    /// <summary>All contributing changes modify existing content.</summary>
    Modified,

    /// <summary>All contributing changes remove content.</summary>
    Deleted,

    /// <summary>Several structural change kinds contribute to the node.</summary>
    Mixed,
}

/// <summary>
/// Records the requested Git baseline and the exact commit it resolved to.
/// </summary>
/// <param name="Kind">The configured baseline mode.</param>
/// <param name="Reference">The human-readable Git reference.</param>
/// <param name="ResolvedSha">The exact resolved commit identifier.</param>
public sealed record GitBaseline(
    GitBaselineKind Kind,
    string Reference,
    string ResolvedSha);

/// <summary>
/// Identifies the current checked-out Git state independently from the comparison baseline.
/// </summary>
/// <param name="HeadSha">The exact current HEAD commit identifier.</param>
/// <param name="Branch">The checked-out branch, or <see langword="null"/> for detached HEAD.</param>
public sealed record GitWorktreeIdentity(
    string HeadSha,
    string? Branch);

/// <summary>
/// Describes one zero-context Git diff hunk.
/// </summary>
/// <param name="OldStart">The one-based start in the baseline file.</param>
/// <param name="OldCount">The number of removed baseline lines.</param>
/// <param name="NewStart">The one-based start in the working-tree file.</param>
/// <param name="NewCount">The number of added working-tree lines.</param>
public sealed record GitHunkDelta(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount);

/// <summary>
/// Carries deterministic Git statistics for one workspace-relative file.
/// </summary>
/// <param name="FilePath">The current workspace-relative path.</param>
/// <param name="PreviousPath">The previous path for a rename, otherwise <see langword="null"/>.</param>
/// <param name="Kind">The file change kind.</param>
/// <param name="Additions">The number of added lines.</param>
/// <param name="Deletions">The number of deleted lines.</param>
/// <param name="IsBinary">Whether Git reported binary content.</param>
/// <param name="Hunks">Zero-context line ranges for mapping current symbols.</param>
public sealed record GitFileDelta(
    string FilePath,
    string? PreviousPath,
    GitFileChangeKind Kind,
    int Additions,
    int Deletions,
    bool IsBinary,
    IReadOnlyList<GitHunkDelta> Hunks);

/// <summary>
/// Carries Git statistics aggregated to one architecture node.
/// </summary>
/// <param name="NodeId">The architecture node identifier.</param>
/// <param name="Kind">The aggregate structural change kind.</param>
/// <param name="Additions">The descendant-aware addition count.</param>
/// <param name="Deletions">The descendant-aware deletion count.</param>
/// <param name="ChangedFiles">The number of distinct contributing files.</param>
/// <param name="Paths">The contributing workspace-relative paths.</param>
public sealed record GitNodeDelta(
    string NodeId,
    GitStructuralChangeKind Kind,
    int Additions,
    int Deletions,
    int ChangedFiles,
    IReadOnlyList<string> Paths);

/// <summary>
/// Keeps Git truth separate from the stable normalized architecture graph.
/// </summary>
/// <param name="Status">Whether the configured Git evidence is available.</param>
/// <param name="Baseline">The resolved baseline when available.</param>
/// <param name="Worktree">The current branch and HEAD identity when available.</param>
/// <param name="Files">All changed files reported by Git.</param>
/// <param name="Nodes">Changes aggregated to architecture nodes.</param>
/// <param name="UnmappedFiles">Changed files that have no project or semantic owner in the graph.</param>
/// <param name="Error">The actionable Git failure when unavailable.</param>
public sealed record GitDeltaOverlay(
    GitDeltaStatus Status,
    GitBaseline? Baseline,
    GitWorktreeIdentity? Worktree,
    IReadOnlyList<GitFileDelta> Files,
    IReadOnlyList<GitNodeDelta> Nodes,
    IReadOnlyList<GitFileDelta> UnmappedFiles,
    string? Error);
