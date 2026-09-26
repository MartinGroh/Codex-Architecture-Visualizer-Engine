using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>
/// Supplies a short, synthetic public conversation for the explicit browser demo mode.
/// </summary>
/// <param name="timeProvider">The clock used to present a current demo conversation.</param>
public sealed class DemoConversationStore(TimeProvider timeProvider) : IConversationStore
{
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
                    "Show the impact of the projection changes and keep the architecture boundaries clear.",
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
                    """
                    The API still depends inward. All values in this conversation are synthetic.

                    | Evidence | Owner |
                    | --- | --- |
                    | Architecture | CodeGraph |
                    | Changes | Git |

                    - [x] Keep evidence separate

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
            ConversationControl.Unavailable,
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
