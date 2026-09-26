using System.Text.Json;
using Cave.Domain;
using Cave.Infrastructure.Conversation;

namespace Cave.Tests;

/// <summary>
/// Verifies the explicit opt-in and bounded public conversation journal contract.
/// </summary>
public sealed class FileConversationStoreTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(
        Path.GetTempPath(),
        "cave-conversation-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes an isolated workspace.
    /// </summary>
    public FileConversationStoreTests() => Directory.CreateDirectory(_workspaceRoot);

    /// <summary>
    /// Verifies that retained files remain invisible until the workspace explicitly opts in.
    /// </summary>
    [Fact]
    public async Task ReadAsyncFailsClosedWhenSharingIsDisabled()
    {
        await WriteMessageAsync("sensitive prompt");
        var store = new FileConversationStore();

        var overlay = await store.ReadAsync(_workspaceRoot, CancellationToken.None);

        Assert.False(overlay.SharingEnabled);
        Assert.Equal(ConversationSourceStatus.Disabled, overlay.Status);
        Assert.Empty(overlay.Messages);
    }

    /// <summary>
    /// Verifies that the supported settings record exposes validated public messages.
    /// </summary>
    [Fact]
    public async Task SetSharingAsyncEnablesValidatedMessages()
    {
        var store = new FileConversationStore();
        await WriteMessageAsync("Explain the host boundary.");
        await store.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);

        var overlay = await store.ReadAsync(_workspaceRoot, CancellationToken.None);

        Assert.True(overlay.SharingEnabled);
        Assert.Equal(ConversationSourceStatus.Ready, overlay.Status);
        var message = Assert.Single(overlay.Messages);
        Assert.Equal(ConversationRole.User, message.Role);
        Assert.Equal("Explain the host boundary.", message.Text);
    }

    /// <summary>
    /// Verifies that hook records from a marked temporary fork never enter the public transcript.
    /// </summary>
    [Fact]
    public async Task ReadAsyncExcludesMarkedEphemeralSession()
    {
        var store = new FileConversationStore();
        await WriteMessageAsync("Temporary side-chat prompt");
        await EphemeralConversationSession.MarkAsync(
            _workspaceRoot,
            "session-1",
            "main-session",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await store.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);

        var overlay = await store.ReadAsync(_workspaceRoot, CancellationToken.None);

        Assert.True(overlay.SharingEnabled);
        Assert.Empty(overlay.Messages);
    }

    /// <summary>
    /// Verifies that malformed settings never activate content sharing.
    /// </summary>
    [Fact]
    public async Task InvalidSettingsRemainDisabledAndVisibleAsDegraded()
    {
        var settingsDirectory = Path.Combine(_workspaceRoot, ".cave", "conversation");
        Directory.CreateDirectory(settingsDirectory);
        await File.WriteAllTextAsync(Path.Combine(settingsDirectory, "settings.json"), "{ invalid");
        var store = new FileConversationStore();

        var overlay = await store.ReadAsync(_workspaceRoot, CancellationToken.None);

        Assert.False(overlay.SharingEnabled);
        Assert.Equal(ConversationSourceStatus.Degraded, overlay.Status);
        Assert.NotNull(overlay.Error);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private async Task WriteMessageAsync(string text)
    {
        var directory = Path.Combine(_workspaceRoot, ".cave", "conversation", "inbox");
        Directory.CreateDirectory(directory);
        var payload = new
        {
            schemaVersion = 1,
            eventId = "event-1",
            occurredAtUtc = "2026-08-19T18:00:00Z",
            sessionId = "session-1",
            turnId = "turn-1",
            agentId = (string?)null,
            agentType = (string?)null,
            isSubagent = false,
            role = "User",
            kind = "Prompt",
            text,
            isTruncated = false,
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, "20260819180000000000-event-1.json"),
            JsonSerializer.Serialize(payload));
    }
}
