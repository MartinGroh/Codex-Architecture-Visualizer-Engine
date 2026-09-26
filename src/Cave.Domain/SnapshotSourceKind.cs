namespace Cave.Domain;

/// <summary>
/// Identifies the source used to produce an architecture snapshot.
/// </summary>
public enum SnapshotSourceKind
{
    /// <summary>An explicitly configured sample used for an acceptance spike.</summary>
    Sample,

    /// <summary>A live CodeGraph semantic index.</summary>
    CodeGraph,
}
