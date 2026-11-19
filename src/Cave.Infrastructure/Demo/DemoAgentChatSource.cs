using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Demo;

/// <summary>Supplies explicitly synthetic public chat and artifacts in the CAVE demo.</summary>
/// <param name="clock">The clock used to position synthetic messages at startup.</param>
public sealed class DemoAgentChatSource(TimeProvider clock) : ICodexAgentChatSource
{
    private static readonly byte[] SamplePng = Convert.FromBase64String("""
        iVBORw0KGgoAAAANSUhEUgAAAMAAAABwCAIAAADHdOglAAABZElEQVR42u3dsQ2DMBRFURahYQUKFmEVdqCjoWcgeoo0GQLS
        pEl2+PrCSnye7gTWkaksmq4dpHCNIxBAAkgACSAJIAEkgASQBJAAEkACSAJIAAkgASQBJIBUGNB1vqVwAAkgASSABJAEkAAS
        QAJIygH0fLxUYZmAPlbZADKADCADCCCAADKAalu/LokBBBBABpABBBBAAAEEkAFkAAEEEEAAARTZNO6JAQQQQAABBBBAAAEE
        EEAAAQTQnwCSd2FuIDeQTxhAAP0uoG0+EgMIIIAAAggggAACCCCAAAIIIIAAAggggAACKALo5iMGCCCAAAIIIIAAAggggAAC
        CCCAAAIIIIAAAsizHnnW4wZyAwEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAPnhXL0DyAAy
        gAwggAACyMoCkndhcUASQAJIAAkgCSABJIAEkACSwn0BnYhPQrRB6/cAAAAASUVORK5CYII=
        """);
    private readonly DateTimeOffset _startedAt = clock.GetUtcNow();

    /// <inheritdoc />
    public Task<CodexAgentChatBatch> ReadAsync(string rootThreadId, IReadOnlyCollection<string> candidateThreadIds,
        DateTimeOffset sharedSinceUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (rootThreadId != "demo-main")
            return Task.FromResult(new CodexAgentChatBatch(rootThreadId, CodexAgentChatStatus.Unavailable, []));

        return Task.FromResult(new CodexAgentChatBatch(rootThreadId, CodexAgentChatStatus.Available,
        [
            new(rootThreadId, null, CodexAgentChatStatus.Available,
            [
                new("demo-chat-root-prompt", "turn-7", null, ConversationRole.User, ConversationMessageKind.Prompt,
                    _startedAt.AddMinutes(-7), "Review how the UI receives the workspace snapshot and what to check when the event stream reconnects.", false, []),
                Published("demo-chat-root-progress", "I asked the review agent to map the snapshot handoff.", -5),
            ]),
            new("demo-reviewer", rootThreadId, CodexAgentChatStatus.Available,
            [
                Published("demo-chat-review-progress", "I traced the handoff from the application to the viewer.", -4),
                Published("demo-chat-review-diagram", DemoSnapshotHandoff.Text, -3, ConversationMessageKind.Final),
            ]),
            new("demo-docs", rootThreadId, CodexAgentChatStatus.Available,
            [
                Published("demo-chat-docs-image", "Here is the shared raster attachment from this demo.", -2,
                    images: [new("image/png", SamplePng)]),
            ]),
            new("demo-builder", rootThreadId, CodexAgentChatStatus.Available,
            [
                Published("demo-chat-builder", "The single-file build completed.", -1, ConversationMessageKind.Final),
            ]),
        ]));
    }

    private CodexAgentChatEntry Published(string item, string text, int minutes,
        ConversationMessageKind kind = ConversationMessageKind.Commentary, IReadOnlyList<CodexAgentChatImage>? images = null) =>
        new(item, "turn-7", "turn-7", ConversationRole.Assistant, kind,
            _startedAt.AddMinutes(minutes), text, false, images ?? []);
}
