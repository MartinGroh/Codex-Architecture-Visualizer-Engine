using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Reads the opt-in public conversation journal and controls its workspace-local sharing setting.
/// </summary>
public interface IConversationStore
{
    /// <summary>Reads the sharing decision and native-history start without loading stored messages.</summary>
    async Task<ConversationSharingState> ReadSharingAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var overlay = await ReadAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return new ConversationSharingState(overlay.SharingEnabled, null);
    }

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

/// <summary>Identifies the current consent to share conversation and, optionally, published native history.</summary>
/// <param name="Enabled">Whether public conversation can be displayed for this workspace.</param>
/// <param name="AgentChatPeriod">The explicit native-history sharing period, absent for legacy opt-in.</param>
public sealed record ConversationSharingState(bool Enabled, AgentChatSharingPeriod? AgentChatPeriod);

/// <summary>Bounds native history to one explicit, revocable workspace opt-in.</summary>
/// <param name="Id">The unique identity of the sharing period, changing after revocation.</param>
/// <param name="StartedAtUtc">The earliest permitted publication timestamp.</param>
public sealed record AgentChatSharingPeriod(Guid Id, DateTimeOffset StartedAtUtc);
