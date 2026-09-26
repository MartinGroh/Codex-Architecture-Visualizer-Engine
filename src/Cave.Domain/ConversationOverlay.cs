namespace Cave.Domain;

/// <summary>
/// Describes whether opt-in conversation evidence is available for a workspace.
/// </summary>
public enum ConversationSourceStatus
{
    /// <summary>Conversation sharing is disabled for the workspace.</summary>
    Disabled,

    /// <summary>The enabled conversation journal is readable.</summary>
    Ready,

    /// <summary>Some conversation evidence was readable, but one or more records failed validation.</summary>
    Degraded,
}

/// <summary>
/// Identifies the public participant represented by a retained conversation message.
/// </summary>
public enum ConversationRole
{
    /// <summary>The message is a user prompt submitted to Codex.</summary>
    User,

    /// <summary>The message is Codex's final assistant response for a turn.</summary>
    Assistant,
}

/// <summary>
/// Identifies the public message boundary retained by the opt-in bridge.
/// </summary>
public enum ConversationMessageKind
{
    /// <summary>The message begins a user turn.</summary>
    Prompt,

    /// <summary>The message is public progress commentary emitted while a turn is running.</summary>
    Commentary,

    /// <summary>The message is a final assistant response.</summary>
    Final,
}

/// <summary>
/// Carries one public user prompt or final assistant message captured by an enabled workspace hook.
/// </summary>
/// <param name="EventId">The unique hook-event identifier.</param>
/// <param name="SessionId">The owning Codex session identifier, when supplied by the host.</param>
/// <param name="TurnId">The owning Codex turn identifier, when supplied by the host.</param>
/// <param name="AgentId">The subagent identifier, when this message belongs to a child agent.</param>
/// <param name="AgentType">The host-provided agent type, when available.</param>
/// <param name="IsSubagent">Whether the message belongs to a child agent.</param>
/// <param name="Role">The public conversation role.</param>
/// <param name="Kind">The public conversation boundary.</param>
/// <param name="Text">The retained public message text.</param>
/// <param name="IsTruncated">Whether the hook bounded the original message before persistence.</param>
/// <param name="IsStreaming">Whether the message is still receiving App Server deltas.</param>
/// <param name="OccurredAtUtc">The hook observation time.</param>
public sealed record ConversationMessage(
    string EventId,
    string? SessionId,
    string? TurnId,
    string? AgentId,
    string? AgentType,
    bool IsSubagent,
    ConversationRole Role,
    ConversationMessageKind Kind,
    string Text,
    bool IsTruncated,
    bool IsStreaming,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Describes the machine-local Codex task bridge state for one workspace.
/// </summary>
public enum ConversationControlState
{
    /// <summary>No exact Codex task has been observed for the workspace.</summary>
    Unavailable,

    /// <summary>The exact task is known and can accept a queued browser message.</summary>
    Ready,

    /// <summary>At least one browser message is waiting for the current task owner to become idle.</summary>
    Queued,

    /// <summary>CAVE is currently running a queued turn through Codex App Server.</summary>
    Running,

    /// <summary>The latest bridge attempt failed and exposes a diagnostic.</summary>
    Failed,
}

/// <summary>
/// Describes the durable delivery state of one browser-originated message.
/// </summary>
public enum ConversationDeliveryState
{
    /// <summary>The message is durably queued.</summary>
    Queued,

    /// <summary>The message is being processed on the exact Codex task.</summary>
    Running,

    /// <summary>The Codex turn completed.</summary>
    Completed,

    /// <summary>The message could not be delivered.</summary>
    Failed,
}

/// <summary>
/// Carries one durable browser-message delivery without duplicating retained prompt content.
/// </summary>
/// <param name="MessageId">The stable client message identifier.</param>
/// <param name="SessionId">The exact Codex task/session identifier captured when queued.</param>
/// <param name="TurnId">The created Codex turn identifier, when delivery started.</param>
/// <param name="State">The current delivery state.</param>
/// <param name="QueuedAtUtc">When the browser message became durable.</param>
/// <param name="UpdatedAtUtc">When the delivery state last changed.</param>
/// <param name="Error">A delivery diagnostic, or <see langword="null"/>.</param>
public sealed record ConversationDelivery(
    string MessageId,
    string SessionId,
    string? TurnId,
    ConversationDeliveryState State,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Error);

/// <summary>
/// Carries the exact-task binding and durable browser-control status independently from chat content.
/// </summary>
/// <param name="SessionId">The exact Codex task/session identifier, when observed.</param>
/// <param name="TurnId">The latest observed or bridge-owned turn identifier.</param>
/// <param name="State">The current bridge state.</param>
/// <param name="CanSend">Whether the browser may queue a message for this exact task.</param>
/// <param name="Deliveries">Recent durable browser deliveries.</param>
/// <param name="Error">A current bridge diagnostic, or <see langword="null"/>.</param>
public sealed record ConversationControl(
    string? SessionId,
    string? TurnId,
    ConversationControlState State,
    bool CanSend,
    IReadOnlyList<ConversationDelivery> Deliveries,
    string? Error)
{
    /// <summary>Gets the control state before an exact task is observed.</summary>
    public static ConversationControl Unavailable { get; } = new(
        SessionId: null,
        TurnId: null,
        ConversationControlState.Unavailable,
        CanSend: false,
        Deliveries: [],
        Error: "No exact Codex task has been observed for this workspace yet.");
}

/// <summary>
/// Carries the opt-in conversation journal independently from semantic, Git, and activity evidence.
/// </summary>
/// <param name="SharingEnabled">Whether this workspace explicitly enabled public conversation sharing.</param>
/// <param name="Status">The conversation source health.</param>
/// <param name="Messages">Retained public user prompts and final assistant messages.</param>
/// <param name="Control">The machine-local exact-task control bridge state.</param>
/// <param name="Error">A journal read error, or <see langword="null"/> when current.</param>
public sealed record ConversationOverlay(
    bool SharingEnabled,
    ConversationSourceStatus Status,
    IReadOnlyList<ConversationMessage> Messages,
    ConversationControl Control,
    string? Error)
{
    /// <summary>Gets the privacy-preserving default for a workspace.</summary>
    public static ConversationOverlay Disabled { get; } = new(
        SharingEnabled: false,
        ConversationSourceStatus.Disabled,
        [],
        ConversationControl.Unavailable,
        Error: null);
}
