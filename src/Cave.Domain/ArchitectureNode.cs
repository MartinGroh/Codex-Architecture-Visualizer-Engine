namespace Cave.Domain;

/// <summary>
/// Represents one stable item in the normalized architecture hierarchy.
/// </summary>
/// <param name="Id">The stable project-aware node identifier.</param>
/// <param name="Kind">The architectural node kind.</param>
/// <param name="Name">The short display name.</param>
/// <param name="ParentId">The containing node identifier, when present.</param>
/// <param name="QualifiedName">The qualified semantic name, when available.</param>
/// <param name="Description">A concise source-backed description.</param>
/// <param name="CategoryId">The user-owned architecture category identifier.</param>
/// <param name="Tags">Stable classification tags.</param>
/// <param name="SourceLocations">Source declarations that contribute to this logical node.</param>
public sealed record ArchitectureNode(
    string Id,
    ArchitectureNodeKind Kind,
    string Name,
    string? ParentId,
    string? QualifiedName,
    string? Description,
    string? CategoryId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SourceLocation> SourceLocations);
