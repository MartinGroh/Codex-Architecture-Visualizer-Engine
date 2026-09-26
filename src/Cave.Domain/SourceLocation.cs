namespace Cave.Domain;

/// <summary>
/// Locates a semantic declaration in a workspace-relative source file.
/// </summary>
/// <param name="FilePath">The workspace-relative source path.</param>
/// <param name="StartLine">The one-based inclusive start line.</param>
/// <param name="EndLine">The one-based inclusive end line.</param>
public sealed record SourceLocation(string FilePath, int StartLine, int EndLine);
