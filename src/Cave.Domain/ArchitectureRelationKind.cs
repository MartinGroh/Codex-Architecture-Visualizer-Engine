namespace Cave.Domain;

/// <summary>
/// Identifies the semantic meaning of an architecture graph relation.
/// </summary>
public enum ArchitectureRelationKind
{
    /// <summary>A parent contains a child.</summary>
    Contains,

    /// <summary>A consumer depends on a provider.</summary>
    DependsOn,

    /// <summary>A derived type inherits from a base type.</summary>
    Inherits,

    /// <summary>A type implements an interface.</summary>
    Implements,

    /// <summary>A project references another project.</summary>
    ProjectReference,

    /// <summary>A client consumes HTTP or server-sent-event routes exposed by a provider.</summary>
    HttpApi,
}
