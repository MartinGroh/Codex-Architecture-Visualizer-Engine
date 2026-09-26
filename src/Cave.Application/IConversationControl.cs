using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Resolves the exact Codex task currently associated with a workspace.
/// </summary>
public interface ICodexTaskLocator
{
    /// <summary>
    /// Resolves the latest main-agent task identity from authoritative hook evidence.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The exact task binding, or <see langword="null"/> when none has been observed.</returns>
    Task<CodexTaskBinding?> ResolveAsync(string workspaceRoot, CancellationToken cancellationToken);
}

/// <summary>
/// Queues trusted-network browser messages for the exact Codex task bound to a workspace.
/// </summary>
public interface IConversationControl
{
    /// <summary>
    /// Durably queues a browser message after verifying the expected task identity.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="expectedSessionId">The exact task identity last rendered by the browser.</param>
    /// <param name="text">The user message.</param>
    /// <param name="cancellationToken">Signals that the queue operation should stop.</param>
    /// <returns>The durable delivery receipt.</returns>
    Task<ConversationDelivery> QueueAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs one node-scoped question in an ephemeral fork of the exact bound Codex task.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="expectedSessionId">The exact task identity last rendered by the browser.</param>
    /// <param name="text">The bounded node-evidence prompt.</param>
    /// <param name="cancellationToken">Signals that the temporary side chat should stop.</param>
    /// <returns>The final assistant text produced by the temporary side chat.</returns>
    Task<string> RunNodeMemoAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs one queued prompt on an exact Codex task and publishes public response messages.
/// </summary>
public interface ICodexTurnRunner
{
    /// <summary>
    /// Resumes the exact task, starts one turn, and streams public assistant messages.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="sessionId">The exact Codex task/session identifier.</param>
    /// <param name="clientMessageId">The stable browser message identifier.</param>
    /// <param name="text">The user prompt.</param>
    /// <param name="turnStarted">Persists the created turn identifier before streaming continues.</param>
    /// <param name="cancellationToken">Signals that the turn should stop.</param>
    /// <returns>The completed Codex turn identifier.</returns>
    Task<string> RunAsync(
        string workspaceRoot,
        string sessionId,
        string clientMessageId,
        string text,
        Func<string, CancellationToken, Task> turnStarted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Forks the exact task in memory, runs one isolated turn, and returns its final response.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="sessionId">The exact Codex task/session identifier to fork.</param>
    /// <param name="text">The isolated prompt.</param>
    /// <param name="cancellationToken">Signals that the temporary side chat should stop.</param>
    /// <returns>The final assistant response without publishing it to the shared conversation.</returns>
    Task<string> RunEphemeralAsync(
        string workspaceRoot,
        string sessionId,
        string text,
        CancellationToken cancellationToken);
}

/// <summary>
/// Identifies the exact Codex task and whether another owner is currently running it.
/// </summary>
/// <param name="SessionId">The exact Codex thread/session identifier.</param>
/// <param name="TurnId">The latest observed turn identifier.</param>
/// <param name="IsActive">Whether fresh hook evidence shows an active owner.</param>
/// <param name="ObservedAtUtc">When the binding was last observed.</param>
public sealed record CodexTaskBinding(
    string SessionId,
    string? TurnId,
    bool IsActive,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Raised when a browser request no longer targets the exact task bound to the workspace.
/// </summary>
public sealed class CodexTaskConflictException : InvalidOperationException
{
    /// <summary>Initializes a task identity conflict.</summary>
    /// <param name="message">The operator-facing diagnostic.</param>
    public CodexTaskConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised before a browser prompt is accepted when another Codex client still owns the task writer.
/// </summary>
/// <remarks>
/// This condition is safe to retry because the App Server rejected task resume before CAVE sent
/// <c>turn/start</c>.
/// </remarks>
public sealed class CodexTaskWriterBusyException : InvalidOperationException
{
    /// <summary>Initializes a transient task-writer ownership conflict.</summary>
    /// <param name="message">The operator-facing diagnostic.</param>
    public CodexTaskWriterBusyException(string message)
        : base(message)
    {
    }
}
