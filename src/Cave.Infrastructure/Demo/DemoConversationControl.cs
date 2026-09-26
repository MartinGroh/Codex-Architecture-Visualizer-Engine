using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Rejects browser control in the fixed synthetic demo so it cannot be mistaken for a live Codex task.
/// </summary>
public sealed class DemoConversationControl : IConversationControl
{
    /// <inheritdoc />
    public Task<ConversationDelivery> QueueAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken) =>
        throw new CodexTaskConflictException("The fixed CAVE demo is not connected to a Codex task.");

    /// <inheritdoc />
    public Task<string> RunNodeMemoAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken) =>
        throw new CodexTaskConflictException("The fixed CAVE demo is not connected to a Codex task.");
}
