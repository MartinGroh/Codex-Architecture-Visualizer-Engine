using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Reads the opt-in public conversation journal and controls its workspace-local sharing setting.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Reads the conversation overlay without exposing content when sharing is disabled.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current conversation overlay.</returns>
    Task<ConversationOverlay> ReadAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Enables or disables public conversation capture for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="enabled">Whether future public user and assistant messages may be retained.</param>
    /// <param name="cancellationToken">Signals that the write should stop.</param>
    Task SetSharingAsync(string workspaceRoot, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates or updates one public conversation message.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="message">The validated public message.</param>
    /// <param name="cancellationToken">Signals that the write should stop.</param>
    Task UpsertAsync(
        string workspaceRoot,
        ConversationMessage message,
        CancellationToken cancellationToken);
}
