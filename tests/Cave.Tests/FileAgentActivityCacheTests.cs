using System.Text.Json;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Conversation;

namespace Cave.Tests;

/// <summary>Verifies that decoded journal reuse preserves live evidence, task routing and privacy.</summary>
public sealed class FileAgentActivityCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly ArchitectureGraph EmptyGraph = ArchitectureGraph.Create([], []);

    /// <summary>An unchanged published record can be reused without reopening its bytes.</summary>
    [Fact]
    public async Task UnchangedPublishedEventIsNotReopened()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new Workspace();
        var path = await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>Atomic replacement changes a task binding immediately, including equal-size records.</summary>
    [Fact]
    public async Task ReplacementAndDeletionInvalidateCachedRecords()
    {
        using var workspace = new Workspace();
        var path = await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        var originalLength = new FileInfo(path).Length;
        var replacement = await workspace.WriteEventAsync("replacement.tmp", "task-b", Now.AddSeconds(1));
        Assert.Equal(originalLength, new FileInfo(replacement).Length);
        File.Move(replacement, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(1).UtcDateTime);
        Assert.Equal("task-b", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        File.Delete(path);
        Assert.Null(await store.ResolveAsync(workspace.Root, CancellationToken.None));
    }

    /// <summary>A new event enters an already-cached workspace without waiting for a TTL.</summary>
    [Fact]
    public async Task NewEventIsVisibleImmediately()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("first.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        await workspace.WriteEventAsync("second.json", "task-b", Now.AddSeconds(1));
        Assert.Equal("task-b", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>Privacy exclusion is re-evaluated when a marker arrives after its cached event.</summary>
    [Fact]
    public async Task LaterEphemeralMarkerExcludesAnAlreadyDecodedEvent()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("first.json", "task-main", Now);
        await workspace.WriteEventAsync("second.json", "task-memo", Now.AddSeconds(1));
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.Equal("task-memo", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        await EphemeralConversationSession.MarkAsync(workspace.Root, "task-memo", "task-main", Now.AddSeconds(2), CancellationToken.None);
        Assert.Equal("task-main", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>Reusing decoded records does not freeze the time-based active-state lease.</summary>
    [Fact]
    public async Task CachedEventStillExpiresByTheCurrentClock()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("event.json", "task-a", Now);
        var clock = new Clock(Now);
        var store = new FileAgentActivityStore(clock);
        Assert.Equal(AgentWorkState.Active, Assert.Single((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Agents).State);
        clock.Now = Now.AddMinutes(6);
        Assert.NotEqual(AgentWorkState.Active, Assert.Single((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Agents).State);
    }

    /// <summary>Malformed data remains visible and a repaired record clears its cached diagnostic.</summary>
    [Fact]
    public async Task RepairedMalformedRecordIsRetried()
    {
        using var workspace = new Workspace();
        var path = Path.Combine(workspace.Inbox, "event.json");
        await File.WriteAllTextAsync(path, "{");
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.NotNull((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Error);
        await workspace.WriteEventAsync("event.json", "task-a", Now);
        Assert.Null((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Error);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>A sharing failure is not cached after the other process releases the unchanged file.</summary>
    [Fact]
    public async Task TransientSharingFailureIsNotCached()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new Workspace();
        var path = await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.NotNull((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Error);
        Assert.Null((await store.ReadAsync(workspace.Root, EmptyGraph, CancellationToken.None)).Error);
        Assert.Equal("task-a", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>An already-cancelled read does not return a cached task binding.</summary>
    [Fact]
    public async Task CachedReadHonorsCancellation()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        await store.ResolveAsync(workspace.Root, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ResolveAsync(workspace.Root, cancelled.Token));
    }

    /// <summary>Concurrent overlay and task readers share decoded records without losing updates.</summary>
    [Fact]
    public async Task ConcurrentReadersObserveTheSameReplacement()
    {
        using var workspace = new Workspace();
        var path = await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(new Clock(Now.AddSeconds(1)));
        await store.ResolveAsync(workspace.Root, CancellationToken.None);
        var replacement = await workspace.WriteEventAsync("replacement.tmp", "task-b", Now.AddSeconds(1));
        File.Move(replacement, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, Now.AddMinutes(1).UtcDateTime);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            Assert.Equal("task-b", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
            Assert.Equal("session:task-b", Assert.Single((await store.ReadAsync(
                workspace.Root, EmptyGraph, CancellationToken.None)).Agents).AgentId);
        }));
    }

    /// <summary>Evicting a cold workspace preserves its ability to discover new task evidence.</summary>
    [Fact]
    public async Task WorkspaceEvictionReloadsCurrentEvidence()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("event.json", "task-a", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        await store.ResolveAsync(workspace.Root, CancellationToken.None);
        for (var index = 0; index < 64; index++)
        {
            var otherRoot = Path.Combine(workspace.Root, $"other-{index}");
            Directory.CreateDirectory(FileAgentActivityStore.GetActivityDirectory(otherRoot));
            Assert.Null(await store.ResolveAsync(otherRoot, CancellationToken.None));
        }
        await workspace.WriteEventAsync("new.json", "task-b", Now.AddSeconds(1));
        Assert.Equal("task-b", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    /// <summary>A new retained event displaces the oldest cached record at the journal count bound.</summary>
    [Fact]
    public async Task RetentionDisplacesCachedRecordsOutsideTheNewest2048Files()
    {
        using var workspace = new Workspace();
        await workspace.WriteEventAsync("0000.json", "displaced-task", Now.AddSeconds(1));
        for (var index = 1; index < 2048; index++)
            await workspace.WriteEventAsync($"{index:D4}.json", "retained-task", Now);
        var store = new FileAgentActivityStore(TimeProvider.System);
        Assert.Equal("displaced-task", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
        await workspace.WriteEventAsync("2048.json", "retained-task", Now);
        Assert.Equal("retained-task", (await store.ResolveAsync(workspace.Root, CancellationToken.None))!.SessionId);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Workspace : IDisposable
    {
        private readonly string _prefix = Path.Combine(Path.GetTempPath(), "cave-activity-cache-tests-");
        internal string Root { get; }
        internal string Inbox { get; }

        internal Workspace()
        {
            Root = _prefix + Guid.NewGuid().ToString("N");
            Inbox = FileAgentActivityStore.GetActivityDirectory(Root);
            Directory.CreateDirectory(Inbox);
        }

        internal async Task<string> WriteEventAsync(string name, string session, DateTimeOffset occurred)
        {
            var path = Path.Combine(Inbox, name);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, eventId = session, kind = "SessionStart", occurredAtUtc = occurred,
                sessionId = session, turnId = "turn-1", workspaceRoot = Root, isSubagent = false,
                paths = Array.Empty<string>(), summary = "Session started", phase = "Thinking",
            }));
            return path;
        }

        /// <summary>Removes only this test's explicitly created temporary workspace.</summary>
        public void Dispose()
        {
            if (!Path.GetFullPath(Root).StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its temporary workspace prefix.");
            Directory.Delete(Root, recursive: true);
        }
    }
}
