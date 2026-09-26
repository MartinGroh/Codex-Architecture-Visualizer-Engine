namespace Cave.Domain;

/// <summary>
/// Identifies the architectural role represented by a graph node.
/// </summary>
public enum ArchitectureNodeKind
{
    /// <summary>A user-owned architecture grouping.</summary>
    ArchitectureGroup,

    /// <summary>A buildable project.</summary>
    Project,

    /// <summary>A hierarchical namespace.</summary>
    Namespace,

    /// <summary>A concrete class.</summary>
    Class,

    /// <summary>An interface contract.</summary>
    Interface,

    /// <summary>An abstract class.</summary>
    AbstractClass,
}
