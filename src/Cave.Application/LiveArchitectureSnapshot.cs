using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Carries a versioned architecture snapshot produced by a live workspace monitor.
/// </summary>
/// <param name="Version">The monotonically increasing workspace version.</param>
/// <param name="Snapshot">The current normalized architecture snapshot.</param>
/// <param name="ChangedPaths">Workspace-relative paths that triggered this refresh.</param>
/// <param name="ActivityChanged">Whether agent activity or declared scope triggered this refresh.</param>
/// <param name="ConversationChanged">Whether opt-in public conversation evidence triggered this refresh.</param>
/// <param name="ObservedAtUtc">The time the monitor published the update.</param>
/// <param name="RefreshError">The latest refresh failure, or <see langword="null"/> when current.</param>
public sealed record LiveArchitectureSnapshot(
    long Version,
    ArchitectureSnapshot Snapshot,
    IReadOnlyList<string> ChangedPaths,
    bool ActivityChanged,
    bool ConversationChanged,
    DateTimeOffset ObservedAtUtc,
    string? RefreshError);
