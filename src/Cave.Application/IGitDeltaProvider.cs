using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Defines the application-owned boundary for deterministic Git change evidence.
/// </summary>
public interface IGitDeltaProvider
{
    /// <summary>
    /// Reads file and hunk changes relative to one explicit baseline.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="baseline">The explicit baseline request.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The resolved baseline and current working-tree changes.</returns>
    /// <exception cref="GitDeltaUnavailableException">The configured Git evidence cannot be read.</exception>
    Task<GitDeltaResult> ReadAsync(
        string workspaceRoot,
        GitBaselineRequest baseline,
        CancellationToken cancellationToken);
}

/// <summary>
/// Requests one explicit Git baseline without defining an alternate fallback.
/// </summary>
/// <param name="Kind">The baseline selection mode.</param>
/// <param name="Commit">The required revision when <paramref name="Kind"/> is <see cref="GitBaselineKind.Commit"/>.</param>
public sealed record GitBaselineRequest(GitBaselineKind Kind, string? Commit)
{
    /// <summary>
    /// Gets the default v1 request for the repository's tracked upstream branch.
    /// </summary>
    public static GitBaselineRequest Upstream { get; } = new(GitBaselineKind.Upstream, null);
}

/// <summary>
/// Carries raw deterministic Git evidence from an infrastructure adapter.
/// </summary>
/// <param name="Baseline">The exact resolved baseline.</param>
/// <param name="Worktree">The exact current branch and HEAD identity.</param>
/// <param name="Files">The changed working-tree files.</param>
public sealed record GitDeltaResult(
    GitBaseline Baseline,
    GitWorktreeIdentity Worktree,
    IReadOnlyList<GitFileDelta> Files);

/// <summary>
/// Reports that the requested Git evidence is unavailable without changing baselines.
/// </summary>
public sealed class GitDeltaUnavailableException : Exception
{
    /// <summary>
    /// Initializes a Git evidence failure.
    /// </summary>
    /// <param name="message">The actionable failure description.</param>
    public GitDeltaUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a Git evidence failure while preserving its infrastructure cause.
    /// </summary>
    /// <param name="message">The actionable failure description.</param>
    /// <param name="innerException">The original infrastructure exception.</param>
    public GitDeltaUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
