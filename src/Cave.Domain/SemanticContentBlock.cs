namespace Cave.Domain;

/// <summary>
/// Base type for composable semantic content attached to an entity.
/// </summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Kind">The discriminating content kind.</param>
public abstract record SemanticContentBlock(string Id, string Kind);

/// <summary>Stores rich Markdown-capable text.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Text">The semantic text.</param>
public sealed record SemanticTextBlock(string Id, string Text)
    : SemanticContentBlock(Id, "text");

/// <summary>Stores an ordered checklist.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Items">The stable checklist items.</param>
public sealed record SemanticChecklistBlock(
    string Id,
    IReadOnlyList<SemanticChecklistItem> Items)
    : SemanticContentBlock(Id, "checklist");

/// <summary>Stores one stable checklist item.</summary>
/// <param name="Id">The stable item identifier.</param>
/// <param name="Text">The item text.</param>
/// <param name="Completed">Whether the item is complete.</param>
/// <param name="Order">The user-defined order.</param>
public sealed record SemanticChecklistItem(
    string Id,
    string Text,
    bool Completed,
    int Order);

/// <summary>Stores a machine-readable code sample.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Source">The source code.</param>
/// <param name="Language">The syntax language token.</param>
/// <param name="Filename">The optional filename.</param>
public sealed record SemanticCodeBlock(
    string Id,
    string Source,
    string Language,
    string? Filename)
    : SemanticContentBlock(Id, "code");

/// <summary>Stores a machine-readable LaTeX formula.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Source">The LaTeX source.</param>
public sealed record SemanticMathBlock(string Id, string Source)
    : SemanticContentBlock(Id, "math");

/// <summary>References an image asset and accessible description.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="AssetId">The referenced asset identifier.</param>
/// <param name="Caption">The optional caption.</param>
/// <param name="AltText">The required accessible description.</param>
public sealed record SemanticImageBlock(
    string Id,
    string AssetId,
    string? Caption,
    string AltText)
    : SemanticContentBlock(Id, "image");

/// <summary>Stores an editable vector drawing foundation.</summary>
/// <param name="Id">The stable block identifier.</param>
/// <param name="Strokes">The vector strokes.</param>
public sealed record SemanticDrawingBlock(
    string Id,
    IReadOnlyList<SemanticDrawingStroke> Strokes)
    : SemanticContentBlock(Id, "drawing");

/// <summary>Stores one stable vector stroke.</summary>
/// <param name="Id">The stable stroke identifier.</param>
/// <param name="Points">The sampled stroke points.</param>
/// <param name="Color">The stroke colour.</param>
/// <param name="Width">The stroke width.</param>
public sealed record SemanticDrawingStroke(
    string Id,
    IReadOnlyList<SemanticDrawingPoint> Points,
    string Color,
    double Width);

/// <summary>Stores one sampled vector drawing point.</summary>
/// <param name="X">The normalized X coordinate.</param>
/// <param name="Y">The normalized Y coordinate.</param>
/// <param name="Pressure">The normalized input pressure.</param>
public sealed record SemanticDrawingPoint(double X, double Y, double Pressure);
