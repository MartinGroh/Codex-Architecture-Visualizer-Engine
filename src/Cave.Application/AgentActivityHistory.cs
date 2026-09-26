using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Reads the privacy-minimized retained activity history for a workspace.
/// </summary>
public interface IAgentActivityHistoryStore
{
    /// <summary>
    /// Reads bounded, safe event metadata whose timestamps fall inside the requested inclusive window.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="windowStartUtc">The inclusive beginning of the requested time window.</param>
    /// <param name="windowEndUtc">The inclusive end of the requested time window.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The events read and explicit source completeness state.</returns>
    Task<AgentActivityHistoryReadResult> ReadHistoryAsync(
        string workspaceRoot,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// One privacy-minimized Codex activity event retained by the canonical activity journal.
/// </summary>
/// <param name="EventId">The stable identity shared by copies of this event across workspace journals.</param>
/// <param name="OccurredAtUtc">The time the Codex hook or scope declaration recorded the event.</param>
/// <param name="SessionId">The Codex session identity, when the source supplied one.</param>
/// <param name="TurnId">The Codex turn identity, when the source supplied one.</param>
/// <param name="AgentId">The stable main-agent session or subagent identity.</param>
/// <param name="AgentType">The bounded Codex agent type or declared role.</param>
/// <param name="IsSubagent">Whether the event belongs to a subagent.</param>
/// <param name="Action">A generated action summary containing no prompt or tool input.</param>
/// <param name="Phase">The safe phase classification recorded for the event, when known.</param>
/// <param name="Evidence">Whether the event came from an observed Codex hook or an explicit scope declaration.</param>
public sealed record AgentActivityHistoryEvent(
    string EventId,
    DateTimeOffset OccurredAtUtc,
    string? SessionId,
    string? TurnId,
    string AgentId,
    string? AgentType,
    bool IsSubagent,
    string Action,
    AgentActivityPhase? Phase,
    AgentActivityEvidenceKind Evidence);

/// <summary>
/// Reports a bounded activity history read without exposing journal paths or raw event contents.
/// </summary>
/// <param name="Events">Safe events retained from the requested time window.</param>
/// <param name="IsAvailable">Whether this configured activity source can provide retained history.</param>
/// <param name="IsTruncated">Whether more in-window records existed than the source read bound allowed.</param>
/// <param name="InvalidRecordCount">The number of records that could not be validated for the requested window.</param>
public sealed record AgentActivityHistoryReadResult(
    IReadOnlyList<AgentActivityHistoryEvent> Events,
    bool IsAvailable,
    bool IsTruncated,
    int InvalidRecordCount)
{
    /// <summary>
    /// Gets an explicit unavailable result for a mode that has no retained event journal.
    /// </summary>
    public static AgentActivityHistoryReadResult Unavailable { get; } = new([], false, false, 0);
}
