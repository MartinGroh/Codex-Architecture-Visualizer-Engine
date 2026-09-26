using Cave.Domain;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Application;

/// <summary>
/// Persists the canonical user-authored semantic workspace without owning its validation policy.
/// </summary>
public interface ISemanticWorkspaceStore
{
    /// <summary>
    /// Loads the persisted semantic workspace when one exists.
    /// </summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The persisted workspace, or <see langword="null"/> when no semantic file exists.</returns>
    Task<SemanticWorkspaceModel?> LoadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically replaces the persisted semantic workspace.
    /// </summary>
    /// <param name="workspaceRoot">The absolute source-workspace root.</param>
    /// <param name="workspace">The complete validated aggregate to persist.</param>
    /// <param name="expectedRevision">The revision that must still be durable before replacement.</param>
    /// <param name="cancellationToken">Signals that the write should stop.</param>
    Task SaveAsync(
        string workspaceRoot,
        SemanticWorkspaceModel workspace,
        long expectedRevision,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reports that the semantic workspace cannot be read or written at its persistence boundary.
/// </summary>
public class SemanticWorkspacePersistenceException : Exception
{
    /// <summary>Initializes a persistence failure with boundary context.</summary>
    /// <param name="message">The actionable persistence diagnostic.</param>
    public SemanticWorkspacePersistenceException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a persistence failure while retaining the original cause.</summary>
    /// <param name="message">The actionable persistence diagnostic.</param>
    /// <param name="innerException">The original filesystem or serialization failure.</param>
    public SemanticWorkspacePersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reports that a durable semantic workspace uses a schema this installation cannot consume.
/// </summary>
public sealed class UnsupportedSemanticWorkspaceSchemaException : SemanticWorkspacePersistenceException
{
    /// <summary>Initializes an unsupported-schema failure.</summary>
    /// <param name="actualVersion">The persisted schema version.</param>
    /// <param name="supportedVersion">The only version accepted by the current application.</param>
    public UnsupportedSemanticWorkspaceSchemaException(int actualVersion, int supportedVersion)
        : base($"Semantic workspace schema version {actualVersion} is unsupported; this installation supports version {supportedVersion}.")
    {
        ActualVersion = actualVersion;
        SupportedVersion = supportedVersion;
    }

    /// <summary>Gets the rejected durable schema version.</summary>
    public int ActualVersion { get; }

    /// <summary>Gets the schema version accepted by the current application.</summary>
    public int SupportedVersion { get; }
}

/// <summary>
/// Reports that another process committed semantic state after the caller loaded its candidate base revision.
/// </summary>
public sealed class SemanticWorkspaceConcurrencyException : SemanticWorkspacePersistenceException
{
    /// <summary>Initializes a revision conflict without overwriting the newer durable aggregate.</summary>
    public SemanticWorkspaceConcurrencyException(long expectedRevision, long actualRevision)
        : base($"Semantic workspace revision changed from expected {expectedRevision} to {actualRevision}; the candidate was not written.")
    {
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    /// <summary>Gets the revision on which the rejected candidate was based.</summary>
    public long ExpectedRevision { get; }

    /// <summary>Gets the revision already present at the durable boundary.</summary>
    public long ActualRevision { get; }
}
