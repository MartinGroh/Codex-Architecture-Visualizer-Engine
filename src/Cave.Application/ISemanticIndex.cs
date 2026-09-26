using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Defines the semantic graph contract owned by the application that consumes it.
/// </summary>
public interface ISemanticIndex
{
    /// <summary>
    /// Reads the current normalized semantic graph and its provider metadata.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root to inspect.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current graph with explicit provenance.</returns>
    Task<SemanticIndexResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken);
}

/// <summary>
/// Carries the normalized output of a configured semantic provider.
/// </summary>
/// <param name="Graph">The validated normalized graph.</param>
/// <param name="WorkspaceName">The displayed workspace name.</param>
/// <param name="ProviderId">The configured provider identity.</param>
/// <param name="SourceKind">The provider source kind.</param>
/// <param name="IsLive">Whether this output reflects live workspace data.</param>
public sealed record SemanticIndexResult(
    ArchitectureGraph Graph,
    string WorkspaceName,
    string ProviderId,
    SnapshotSourceKind SourceKind,
    bool IsLive);
