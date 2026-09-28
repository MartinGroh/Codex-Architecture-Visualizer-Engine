using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Conversation;

namespace Cave.Tests;

/// <summary>
/// Verifies durable exact-task browser delivery independently from the real Codex process.
/// </summary>
public sealed class CodexConversationBridgeTests : IDisposable
{
    // CI runs these disk-backed polling tests alongside other host tests. Keep a bounded
    // cancellation deadline that tolerates slow runner scheduling without changing assertions.
    private static readonly TimeSpan BridgeTestDeadline = TimeSpan.FromSeconds(30);
    private readonly string _workspaceRoot = Directory.CreateTempSubdirectory("cave-chat-bridge-").FullName;

    /// <summary>Verifies that stale browser state can never target another Codex task.</summary>
    [Fact]
    public async Task QueueAsyncRejectsMismatchedTaskIdentity()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        using var bridge = CreateBridge(
            conversations,
            new StubTaskLocator(new CodexTaskBinding(
                "current-task",
                "turn-1",
                IsActive: false,
                DateTimeOffset.UtcNow)),
            new StubTurnRunner());

        await Assert.ThrowsAsync<CodexTaskConflictException>(() => bridge.QueueAsync(
            _workspaceRoot,
            "stale-task",
            "Do not deliver this message.",
            CancellationToken.None));
    }

    /// <summary>
    /// Verifies that an isolated node memo uses the exact active task without entering the shared queue.
    /// </summary>
    [Fact]
    public async Task RunNodeMemoAsyncUsesEphemeralRunnerWithoutSharedConversation()
    {
        var conversations = new FileConversationStore();
        var runner = new StubTurnRunner();
        var binding = new CodexTaskBinding(
            "exact-task",
            "active-turn",
            IsActive: true,
            DateTimeOffset.UtcNow);
        using var bridge = CreateBridge(
            conversations,
            new StubTaskLocator(binding),
            runner);

        var answer = await bridge.RunNodeMemoAsync(
            _workspaceRoot,
            binding.SessionId,
            "Explain the selected node.",
            CancellationToken.None);

        Assert.Equal("Temporary answer.", answer);
        Assert.Equal(binding.SessionId, runner.SessionId);
        Assert.Equal("Explain the selected node.", runner.Text);
        var overlay = await conversations.ReadAsync(_workspaceRoot, CancellationToken.None);
        Assert.Empty(overlay.Messages);
        Assert.Empty(overlay.Control.Deliveries);
    }

    /// <summary>Verifies that repeated browser clicks cannot create duplicate queued prompts.</summary>
    [Fact]
    public async Task QueueAsyncIsSingleFlightAndIdempotentForTheSameText()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var binding = new CodexTaskBinding(
            "exact-task",
            "turn-1",
            IsActive: true,
            DateTimeOffset.UtcNow);
        using var bridge = CreateBridge(
            conversations,
            new StubTaskLocator(binding),
            new StubTurnRunner());

        var first = await bridge.QueueAsync(
            _workspaceRoot,
            binding.SessionId,
            "Explain Cave.Application.",
            CancellationToken.None);
        var duplicate = await bridge.QueueAsync(
            _workspaceRoot,
            binding.SessionId,
            "Explain Cave.Application.",
            CancellationToken.None);

        Assert.Equal(first.MessageId, duplicate.MessageId);
        var overlay = await conversations.ReadAsync(_workspaceRoot, CancellationToken.None);
        Assert.Single(overlay.Control.Deliveries);
        Assert.False(overlay.Control.CanSend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.QueueAsync(
            _workspaceRoot,
            binding.SessionId,
            "Explain cave-ui instead.",
            CancellationToken.None));
    }

    /// <summary>
    /// Verifies that a browser message becomes durable, starts only on the exact task, and reaches completion.
    /// </summary>
    [Fact]
    public async Task RunAsyncCompletesDurablyQueuedMessageOnExactTask()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var runner = new StubTurnRunner();
        var binding = new CodexTaskBinding(
            "exact-task",
            "desktop-turn",
            IsActive: false,
            DateTimeOffset.UtcNow);
        using var bridge = CreateBridge(conversations, new StubTaskLocator(binding), runner);
        // This test covers consumption of a durable queue. Finish enqueueing before
        // starting the worker so Queued cannot race the stub's immediate completion.
        var queued = await bridge.QueueAsync(
            _workspaceRoot,
            binding.SessionId,
            "Continue from the remote browser.",
            CancellationToken.None);
        var persisted = await conversations.ReadAsync(_workspaceRoot, CancellationToken.None);
        Assert.Equal(queued.MessageId, Assert.Single(persisted.Control.Deliveries).MessageId);
        Assert.Equal(ConversationControlState.Queued, persisted.Control.State);

        using var stopping = new CancellationTokenSource(BridgeTestDeadline);
        var background = bridge.RunAsync(stopping.Token);
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            ConversationOverlay overlay;
            do
            {
                await Task.Delay(25, stopping.Token);
                overlay = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
            }
            // Delivery and control status are separate durable writes. Completion can
            // become visible before Ready, especially on the Windows CI filesystem.
            while (overlay.Control.State != ConversationControlState.Ready
                || overlay.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Completed));

            var delivery = Assert.Single(overlay.Control.Deliveries);
            Assert.Equal(queued.MessageId, delivery.MessageId);
            Assert.Equal("exact-task", delivery.SessionId);
            Assert.Equal("bridge-turn", delivery.TurnId);
            Assert.Equal(ConversationDeliveryState.Completed, delivery.State);
            Assert.Equal(ConversationControlState.Ready, overlay.Control.State);
            Assert.True(overlay.Control.CanSend);
            Assert.Equal("Continue from the remote browser.", runner.Text);
            Assert.Equal("exact-task", runner.SessionId);

            var immediateReplay = await bridge.QueueAsync(
                _workspaceRoot,
                binding.SessionId,
                "Continue from the remote browser.",
                CancellationToken.None);
            Assert.Equal(queued.MessageId, immediateReplay.MessageId);
            Assert.Equal(ConversationDeliveryState.Completed, immediateReplay.State);
            overlay = await conversations.ReadAsync(_workspaceRoot, CancellationToken.None);
            Assert.Single(overlay.Control.Deliveries);
        }
        finally
        {
            // Stop and join the worker even when an assertion fails, before fixture
            // disposal removes the directory containing its status journal.
            stopping.Cancel();
            await background;
        }
    }

    /// <summary>
    /// Verifies that queued browser work cannot create a second owner while the hook-observed turn is active.
    /// </summary>
    [Fact]
    public async Task RunAsyncWaitsForObservedLifecycleCompletion()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var runner = new StubTurnRunner();
        var locator = new MutableTaskLocator(new CodexTaskBinding(
            "exact-task",
            "desktop-turn",
            IsActive: true,
            DateTimeOffset.UtcNow));
        using var bridge = CreateBridge(conversations, locator, runner);
        using var stopping = new CancellationTokenSource(BridgeTestDeadline);
        var background = bridge.RunAsync(stopping.Token);

        try
        {
            await bridge.QueueAsync(
                _workspaceRoot,
                "exact-task",
                "Wait until the desktop turn ends.",
                stopping.Token);

            // Observe the bridge's active-owner decision before releasing the task.
            // Fixed sleeps and a separate three-second deadline raced CI file I/O.
            ConversationOverlay waiting;
            do
            {
                await Task.Delay(25, stopping.Token);
                waiting = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
            }
            while (waiting.Control.Error is null);

            Assert.False(runner.Started.Task.IsCompleted);
            Assert.Equal(ConversationControlState.Queued, waiting.Control.State);
            Assert.False(waiting.Control.CanSend);
            Assert.Contains("desktop-owned", waiting.Control.Error, StringComparison.Ordinal);

            locator.Binding = locator.Binding with { IsActive = false };
            await runner.Started.Task.WaitAsync(stopping.Token);
        }
        finally
        {
            // Join the status writer before fixture disposal, including failed waits.
            stopping.Cancel();
            await background;
        }
    }

    /// <summary>
    /// Verifies that App Server writer contention remains queued and is retried without losing the prompt.
    /// </summary>
    [Fact]
    public async Task RunAsyncRetriesTaskWriterContentionBeforeTurnStart()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var binding = new CodexTaskBinding(
            "exact-task",
            "desktop-turn",
            IsActive: false,
            DateTimeOffset.UtcNow);
        var locator = new MutableTaskLocator(binding);
        // Keep the observed owner active after contention until the queued journal and status
        // have both been read. A retry may otherwise claim the prompt between those two reads.
        var runner = new BusyThenSucceedTurnRunner(
            () => locator.Binding = locator.Binding with { IsActive = true });
        using var bridge = CreateBridge(conversations, locator, runner);
        using var stopping = new CancellationTokenSource(BridgeTestDeadline);
        var background = bridge.RunAsync(stopping.Token);

        var queued = await bridge.QueueAsync(
            _workspaceRoot,
            binding.SessionId,
            "Retry this exact browser prompt.",
            CancellationToken.None);
        await runner.WriterBusy.Task.WaitAsync(TimeSpan.FromSeconds(3));

        ConversationOverlay waiting;
        do
        {
            await Task.Delay(25, stopping.Token);
            waiting = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
        }
        while (waiting.Control.State != ConversationControlState.Queued
            || waiting.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Queued
                || item.Error is null));

        var waitingDelivery = Assert.Single(waiting.Control.Deliveries);
        Assert.Equal(queued.MessageId, waitingDelivery.MessageId);
        Assert.Equal(ConversationDeliveryState.Queued, waitingDelivery.State);
        Assert.Contains("will retry", waitingDelivery.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConversationControlState.Queued, waiting.Control.State);
        Assert.Null(waitingDelivery.TurnId);
        Assert.False(waiting.Control.CanSend);
        Assert.Equal(1, runner.Attempts);

        runner.AllowSuccess.TrySetResult();
        locator.Binding = locator.Binding with { IsActive = false };
        await runner.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        ConversationOverlay completed;
        do
        {
            await Task.Delay(25, stopping.Token);
            completed = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
        }
        while (completed.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Completed));

        Assert.Equal(2, runner.Attempts);
        Assert.Equal("Retry this exact browser prompt.", runner.Text);

        stopping.Cancel();
        await background;
    }

    /// <summary>
    /// Verifies that the pre-fix active-writer failure is recovered because it has no accepted turn id.
    /// </summary>
    [Fact]
    public async Task RunAsyncRecoversLegacyActiveWriterFailure()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var journal = new ConversationControlJournal(TimeProvider.System);
        var envelope = await journal.QueueSingleFlightAsync(
            _workspaceRoot,
            "exact-task",
            "Recover the message that failed before turn start.",
            CancellationToken.None);
        await journal.UpdateAsync(
            envelope,
            ConversationDeliveryState.Failed,
            turnId: null,
            "Codex task resume failed: thread exact-task already has an active writer.",
            bridgeInstanceId: null,
            CancellationToken.None);
        var runner = new StubTurnRunner();
        var locator = new MutableTaskLocator(new CodexTaskBinding(
            "exact-task",
            "desktop-turn",
            IsActive: true,
            DateTimeOffset.UtcNow));
        using var bridge = CreateBridge(
            conversations,
            locator,
            runner);
        using var stopping = new CancellationTokenSource(BridgeTestDeadline);
        var background = bridge.RunAsync(stopping.Token);

        try
        {
            ConversationOverlay recovered;
            do
            {
                await Task.Delay(25, stopping.Token);
                recovered = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
            }
            while (recovered.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Queued));

            var recoveredDelivery = Assert.Single(recovered.Control.Deliveries);
            Assert.Contains("will retry", recoveredDelivery.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(runner.Started.Task.IsCompleted);

            locator.Binding = locator.Binding with { IsActive = false };
            await runner.Started.Task.WaitAsync(stopping.Token);

            ConversationOverlay completed;
            do
            {
                await Task.Delay(25, stopping.Token);
                completed = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
            }
            while (completed.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Completed));

            Assert.Equal("Recover the message that failed before turn start.", runner.Text);
        }
        finally
        {
            // CONSTRAINT: join the bridge before fixture cleanup even when an assertion times out.
            stopping.Cancel();
            await background;
        }
    }

    /// <summary>
    /// Verifies that stopping the machine host during a turn leaves a recoverable queued envelope.
    /// </summary>
    [Fact]
    public async Task HostRestartRecoversInterruptedDelivery()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var locator = new StubTaskLocator(new CodexTaskBinding(
            "exact-task",
            "desktop-turn",
            IsActive: false,
            DateTimeOffset.UtcNow));
        var interruptedRunner = new BlockingTurnRunner();
        using (var firstBridge = CreateBridge(conversations, locator, interruptedRunner))
        using (var firstStop = new CancellationTokenSource(BridgeTestDeadline))
        {
            var firstBackground = firstBridge.RunAsync(firstStop.Token);
            await firstBridge.QueueAsync(
                _workspaceRoot,
                "exact-task",
                "Recover this message after restart.",
                CancellationToken.None);
            await interruptedRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

            firstStop.Cancel();
            await firstBackground;
        }

        var recovered = await conversations.ReadAsync(_workspaceRoot, CancellationToken.None);
        Assert.Equal(
            ConversationDeliveryState.Queued,
            Assert.Single(recovered.Control.Deliveries).State);

        var completingRunner = new StubTurnRunner();
        using var secondBridge = CreateBridge(conversations, locator, completingRunner);
        using var secondStop = new CancellationTokenSource(BridgeTestDeadline);
        var secondBackground = secondBridge.RunAsync(secondStop.Token);
        await completingRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        ConversationOverlay completed;
        do
        {
            await Task.Delay(25, secondStop.Token);
            completed = await conversations.ReadAsync(_workspaceRoot, secondStop.Token);
        }
        while (completed.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Completed));

        secondStop.Cancel();
        await secondBackground;
    }

    /// <summary>
    /// Verifies that work handed to the exact-task owner hook remains owned by that hook until
    /// its answering Stop completes it; a machine-host scan must not classify it as a crash.
    /// </summary>
    [Fact]
    public async Task OwnerHookRunningDeliveryIsNotRecoveredByMachineHost()
    {
        var journal = new ConversationControlJournal(TimeProvider.System);
        var envelope = await journal.QueueSingleFlightAsync(
            _workspaceRoot,
            "exact-task",
            "Wait for the owner hook answer.",
            CancellationToken.None);
        await journal.UpdateAsync(
            envelope,
            ConversationDeliveryState.Running,
            "claiming-turn",
            error: null,
            bridgeInstanceId: "codex-owner-hook",
            CancellationToken.None);

        var pending = await ConversationControlJournal.ReadPendingAsync(
            _workspaceRoot,
            "new-machine-host",
            CancellationToken.None);

        Assert.Empty(pending);
    }

    /// <summary>
    /// Verifies that an ambiguous hard-crash record is never replayed onto an exact task.
    /// </summary>
    [Fact]
    public async Task NewHostFailsForeignRunningDeliveryWithoutReplay()
    {
        var conversations = new FileConversationStore();
        await conversations.SetSharingAsync(_workspaceRoot, enabled: true, CancellationToken.None);
        var journal = new ConversationControlJournal(TimeProvider.System);
        var envelope = await journal.QueueSingleFlightAsync(
            _workspaceRoot,
            "exact-task",
            "Do not replay after an ambiguous crash.",
            CancellationToken.None);
        await journal.UpdateAsync(
            envelope,
            ConversationDeliveryState.Running,
            "possibly-accepted-turn",
            error: null,
            bridgeInstanceId: "previous-host",
            CancellationToken.None);
        var runner = new StubTurnRunner();
        using var bridge = CreateBridge(
            conversations,
            new StubTaskLocator(new CodexTaskBinding(
                "exact-task",
                "desktop-turn",
                IsActive: false,
                DateTimeOffset.UtcNow)),
            runner);
        using var stopping = new CancellationTokenSource(BridgeTestDeadline);
        var background = bridge.RunAsync(stopping.Token);

        ConversationOverlay overlay;
        do
        {
            await Task.Delay(25, stopping.Token);
            overlay = await conversations.ReadAsync(_workspaceRoot, stopping.Token);
        }
        while (overlay.Control.Deliveries.All(item => item.State != ConversationDeliveryState.Failed));

        Assert.False(runner.Started.Task.IsCompleted);
        Assert.Contains(
            "not replayed",
            Assert.Single(overlay.Control.Deliveries).Error,
            StringComparison.OrdinalIgnoreCase);

        stopping.Cancel();
        await background;
    }

    /// <summary>
    /// Verifies that the one-second bridge scan cannot manufacture live updates from unchanged status.
    /// </summary>
    [Fact]
    public async Task RepeatedEquivalentStatusDoesNotRewriteJournal()
    {
        var journal = new ConversationControlJournal(TimeProvider.System);
        await journal.WriteStatusAsync(
            _workspaceRoot,
            "exact-task",
            "turn-1",
            ConversationControlState.Ready,
            canSend: true,
            error: null,
            CancellationToken.None);
        var statusPath = Path.Combine(
            _workspaceRoot,
            ConversationControlJournal.ControlDirectory.Replace('/', Path.DirectorySeparatorChar),
            "status.json");
        var firstWrite = File.GetLastWriteTimeUtc(statusPath);

        await Task.Delay(100, CancellationToken.None);
        await journal.WriteStatusAsync(
            _workspaceRoot,
            "exact-task",
            "turn-1",
            ConversationControlState.Ready,
            canSend: true,
            error: null,
            CancellationToken.None);

        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(statusPath));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private CodexConversationBridge CreateBridge(
        IConversationStore conversations,
        ICodexTaskLocator taskLocator,
        ICodexTurnRunner turnRunner) =>
        new(
            new StubCatalogStore(_workspaceRoot),
            taskLocator,
            conversations,
            turnRunner,
            TimeProvider.System);

    private sealed class StubCatalogStore(string workspaceRoot) : IWorkspaceCatalogStore
    {
        public Task<WorkspaceCatalogEntry> RegisterAsync(
            string root,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkspaceCatalogReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceCatalogReadResult(
                [new WorkspaceCatalogEntry("workspace", workspaceRoot, DateTimeOffset.UtcNow)],
                []));
    }

    private sealed class StubTaskLocator(CodexTaskBinding binding) : ICodexTaskLocator
    {
        public Task<CodexTaskBinding?> ResolveAsync(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult<CodexTaskBinding?>(binding);
    }

    private sealed class MutableTaskLocator(CodexTaskBinding binding) : ICodexTaskLocator
    {
        public CodexTaskBinding Binding { get; set; } = binding;

        public Task<CodexTaskBinding?> ResolveAsync(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult<CodexTaskBinding?>(Binding);
    }

    private sealed class StubTurnRunner : ICodexTurnRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? SessionId { get; private set; }
        public string? Text { get; private set; }

        public async Task<string> RunAsync(
            string workspaceRoot,
            string sessionId,
            string clientMessageId,
            string text,
            Func<string, CancellationToken, Task> turnStarted,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            Text = text;
            await turnStarted("bridge-turn", cancellationToken);
            Started.TrySetResult();
            return "bridge-turn";
        }

        public Task<string> RunEphemeralAsync(
            string workspaceRoot,
            string sessionId,
            string text,
            CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            Text = text;
            return Task.FromResult("Temporary answer.");
        }
    }

    private sealed class BlockingTurnRunner : ICodexTurnRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> RunAsync(
            string workspaceRoot,
            string sessionId,
            string clientMessageId,
            string text,
            Func<string, CancellationToken, Task> turnStarted,
            CancellationToken cancellationToken)
        {
            await turnStarted("bridge-turn", cancellationToken);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "bridge-turn";
        }

        public Task<string> RunEphemeralAsync(
            string workspaceRoot,
            string sessionId,
            string text,
            CancellationToken cancellationToken) =>
            Task.FromResult("Temporary answer.");
    }

    private sealed class BusyThenSucceedTurnRunner(Action writerBusy) : ICodexTurnRunner
    {
        public TaskCompletionSource WriterBusy { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSuccess { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }
        public string? Text { get; private set; }

        public async Task<string> RunAsync(
            string workspaceRoot,
            string sessionId,
            string clientMessageId,
            string text,
            Func<string, CancellationToken, Task> turnStarted,
            CancellationToken cancellationToken)
        {
            Attempts++;
            Text = text;
            if (Attempts == 1)
            {
                writerBusy();
                WriterBusy.TrySetResult();
                throw new CodexTaskWriterBusyException(
                    "Codex Desktop still owns this task. CAVE will retry when the task writer is released.");
            }

            await AllowSuccess.Task.WaitAsync(cancellationToken);
            await turnStarted("bridge-retried-turn", cancellationToken);
            Completed.TrySetResult();
            return "bridge-retried-turn";
        }

        public Task<string> RunEphemeralAsync(
            string workspaceRoot,
            string sessionId,
            string text,
            CancellationToken cancellationToken) =>
            Task.FromResult("Temporary answer.");
    }
}
