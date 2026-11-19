using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies a synthetic public conversation for the explicit browser demo mode.
/// </summary>
/// <param name="timeProvider">The clock used to present a current demo conversation.</param>
public sealed class DemoConversationStore(TimeProvider timeProvider) : IConversationStore
{
    /// <inheritdoc />
    public Task<ConversationSharingState> ReadSharingAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ConversationSharingState(true,
            new AgentChatSharingPeriod(new Guid("7528d92b-ff13-4c7a-b8ad-cd747cf98b0f"), DateTimeOffset.UnixEpoch)));
    }

    /// <inheritdoc />
    public Task<ConversationOverlay> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();

        return Task.FromResult(new ConversationOverlay(
            SharingEnabled: true,
            ConversationSourceStatus.Ready,
            [
                new ConversationMessage(
                    "demo-message-1",
                    "demo-session",
                    "turn-7",
                    null,
                    "Main agent",
                    IsSubagent: false,
                    ConversationRole.User,
                    ConversationMessageKind.Prompt,
                    "Review how the UI receives the workspace snapshot and what to check when the event stream reconnects.",
                    IsTruncated: false,
                    IsStreaming: false,
                    now.AddMinutes(-7)),
                new ConversationMessage(
                    "demo-message-2",
                    "demo-session",
                    "turn-7",
                    "demo-reviewer",
                    "Review agent",
                    IsSubagent: true,
                    ConversationRole.Assistant,
                    ConversationMessageKind.Final,
                    DemoSnapshotHandoff.Text + "\n\n" + """
                    :::writing{variant="document" id="12345"}
                    **Review note**

                    The projection preserves the architecture boundaries.
                    :::

                    ::code-comment{title="Boundary checked" body="The API depends on the application." file="src/Atlas.Api/SnapshotEndpoints.cs" start=12}

                    - :codex-followup[Review projection]{prompt="Review the projection boundary."}
                    """,
                    IsTruncated: false,
                    IsStreaming: false,
                    now.AddMinutes(-2)),
            ],
            ConversationControl.Unavailable with
            {
                SessionId = "demo-session",
                Error = "The fixed CAVE demo is not connected to a real Codex task.",
            },
            Error: null));
    }

    /// <inheritdoc />
    public Task SetSharingAsync(
        string workspaceRoot,
        bool enabled,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The fixed CAVE demo conversation is read-only.");

    /// <inheritdoc />
    public Task UpsertAsync(
        string workspaceRoot,
        ConversationMessage message,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The fixed CAVE demo conversation is read-only.");
}
