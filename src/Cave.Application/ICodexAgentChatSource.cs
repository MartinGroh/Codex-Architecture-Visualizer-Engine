using Cave.Domain;

namespace Cave.Application;

/// <summary>Reads a bounded batch of published chat from an exact native Codex task and its verified descendants.</summary>
public interface ICodexAgentChatSource
{
    /// <summary>Reads public entries and discovers verified child threads in the selected task.</summary>
    /// <param name="rootThreadId">The exact root thread identified by the workspace.</param>
    /// <param name="candidateThreadIds">Native thread candidates observed by the workspace. The source verifies ancestry before reading items.</param>
    /// <param name="sharedSinceUtc">Only entries published on or after this time may be returned.</param>
    /// <param name="cancellationToken">Signals that the bounded read should stop.</param>
    /// <returns>The exact root and any verified descendants, with per-thread availability.</returns>
    Task<CodexAgentChatBatch> ReadAsync(string rootThreadId, IReadOnlyCollection<string> candidateThreadIds,
        DateTimeOffset sharedSinceUtc, CancellationToken cancellationToken);
}

/// <summary>Indicates how much of the requested published history the native source could read.</summary>
public enum CodexAgentChatStatus
{
    /// <summary>The bounded history was read.</summary>
    Available,

    /// <summary>Some verified history was read before a limit or failure.</summary>
    Partial,

    /// <summary>The native source did not supply public history for the request.</summary>
    Unavailable,
}

/// <summary>The native result for an exact root task.</summary>
/// <param name="RootThreadId">The requested root thread ID.</param>
/// <param name="Status">The batch availability.</param>
/// <param name="Threads">The root and verified descendants represented in the batch.</param>
public sealed record CodexAgentChatBatch(string RootThreadId, CodexAgentChatStatus Status,
    IReadOnlyList<CodexAgentChatThread> Threads);

/// <summary>The public history and verified identity of a single native thread.</summary>
/// <param name="ThreadId">The native thread ID.</param>
/// <param name="ParentThreadId">The verified immediate parent, or null for the selected root.</param>
/// <param name="Status">Availability of the thread's public history.</param>
/// <param name="Entries">Published entries originating in this thread.</param>
public sealed record CodexAgentChatThread(string ThreadId, string? ParentThreadId,
    CodexAgentChatStatus Status, IReadOnlyList<CodexAgentChatEntry> Entries);

/// <summary>A published message or generated artifact from a native thread.</summary>
/// <param name="ItemId">The stable native item ID, scoped to this thread and turn.</param>
/// <param name="TurnId">The exact native turn containing the entry.</param>
/// <param name="RootTurnId">The native root turn when available.</param>
/// <param name="Role">The public author role.</param>
/// <param name="Kind">The prompt, commentary or final-answer phase.</param>
/// <param name="PublishedAtUtc">The native completion timestamp for this published item.</param>
/// <param name="Text">The bounded public message text, or empty for an image-only item.</param>
/// <param name="IsTruncated">Whether the source omitted text beyond its size limit.</param>
/// <param name="Images">Verified inline image bytes or explicit unavailable representations.</param>
public sealed record CodexAgentChatEntry(string ItemId, string TurnId, string? RootTurnId,
    ConversationRole Role, ConversationMessageKind Kind, DateTimeOffset PublishedAtUtc,
    string Text, bool IsTruncated, IReadOnlyList<CodexAgentChatImage> Images);

/// <summary>An image representation from one published native item.</summary>
/// <param name="ContentType">The validated raster media type, or null when unavailable.</param>
/// <param name="Bytes">The bounded raster payload, or null for a reference without an authorized payload.</param>
public sealed record CodexAgentChatImage(string? ContentType, byte[]? Bytes);
