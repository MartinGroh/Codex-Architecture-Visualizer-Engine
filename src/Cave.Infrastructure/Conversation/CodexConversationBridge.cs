using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Conversation;

/// <summary>
/// Durably queues trusted-network browser messages and serially runs them on the exact observed Codex task.
/// </summary>
public sealed class CodexConversationBridge : IConversationControl, IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);
    private const int MaxMessageLength = 32_768;
    private readonly IWorkspaceCatalogStore _catalogStore;
    private readonly ICodexTaskLocator _taskLocator;
    private readonly IConversationStore _conversationStore;
    private readonly ICodexTurnRunner _turnRunner;
    private readonly ConversationControlJournal _journal;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    /// <summary>Initializes the canonical browser-to-Codex task bridge.</summary>
    public CodexConversationBridge(
        IWorkspaceCatalogStore catalogStore,
        ICodexTaskLocator taskLocator,
        IConversationStore conversationStore,
        ICodexTurnRunner turnRunner,
        TimeProvider timeProvider)
    {
        _catalogStore = catalogStore;
        _taskLocator = taskLocator;
        _conversationStore = conversationStore;
        _turnRunner = turnRunner;
        _journal = new ConversationControlJournal(timeProvider);
    }

    /// <inheritdoc />
    public async Task<ConversationDelivery> QueueAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var normalizedText = text.Trim();
        if (normalizedText.Length > MaxMessageLength)
        {
            throw new ArgumentException(
                $"Conversation messages cannot exceed {MaxMessageLength:N0} characters.",
                nameof(text));
        }

        var conversation = await _conversationStore.ReadAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!conversation.SharingEnabled)
        {
            throw new InvalidOperationException(
                "Enable public conversation sharing before sending a browser message.");
        }

        var binding = await _taskLocator.ResolveAsync(workspaceRoot, cancellationToken).ConfigureAwait(false)
            ?? throw new CodexTaskConflictException(
                "CAVE has not observed an exact Codex task for this workspace yet.");
        if (!binding.SessionId.Equals(expectedSessionId.Trim(), StringComparison.Ordinal))
        {
            throw new CodexTaskConflictException(
                "The workspace is now bound to a different Codex task. Refresh before sending.");
        }

        var envelope = await _journal.QueueSingleFlightAsync(
            workspaceRoot,
            binding.SessionId,
            normalizedText,
            cancellationToken).ConfigureAwait(false);
        if (envelope.Record.State == ConversationDeliveryState.Completed)
        {
            return ToDelivery(envelope.Record);
        }

        await _journal.WriteStatusAsync(
            workspaceRoot,
            binding.SessionId,
            binding.TurnId,
            ConversationControlState.Queued,
            canSend: false,
            error: null,
            cancellationToken).ConfigureAwait(false);
        Wake();
        return ToDelivery(envelope.Record);
    }

    /// <inheritdoc />
    public async Task<string> RunNodeMemoAsync(
        string workspaceRoot,
        string expectedSessionId,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var normalizedText = text.Trim();
        if (normalizedText.Length > MaxMessageLength)
        {
            throw new ArgumentException(
                $"Node questions cannot exceed {MaxMessageLength:N0} characters.",
                nameof(text));
        }

        var binding = await _taskLocator.ResolveAsync(workspaceRoot, cancellationToken).ConfigureAwait(false)
            ?? throw new CodexTaskConflictException(
                "CAVE has not observed an exact Codex task for this workspace yet.");
        if (!binding.SessionId.Equals(expectedSessionId.Trim(), StringComparison.Ordinal))
        {
            throw new CodexTaskConflictException(
                "The workspace is now bound to a different Codex task. Refresh before asking again.");
        }

        return await _turnRunner.RunEphemeralAsync(
            workspaceRoot,
            binding.SessionId,
            normalizedText,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the durable delivery loop until the host stops.</summary>
    /// <param name="stoppingToken">Signals that the host is stopping.</param>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError(
                    "The CAVE conversation bridge scan failed: {0}",
                    exception);
            }

            try
            {
                using var timer = new CancellationTokenSource(ScanInterval);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timer.Token);
                await _wake.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var catalog = await _catalogStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var workspace in catalog.Entries.Where(item => Directory.Exists(item.WorkspaceRoot)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ProcessWorkspaceAsync(workspace.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessWorkspaceAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var binding = await _taskLocator.ResolveAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var pending = await ConversationControlJournal.ReadPendingAsync(
                workspaceRoot,
                _instanceId,
                cancellationToken)
            .ConfigureAwait(false);
        if (binding is null)
        {
            if (pending.Count == 0)
            {
                // No hook-bound task and no recoverable work means there is nothing to persist.
                // This also keeps read-only/catalog-only workspaces free of bridge churn.
                return;
            }

            await _journal.WriteStatusAsync(
                workspaceRoot,
                sessionId: null,
                turnId: null,
                ConversationControlState.Unavailable,
                canSend: false,
                "No exact Codex task has been observed for this workspace yet.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (pending.Count == 0)
        {
            if (await ConversationControlJournal.IsOwnerHookDeliveryRunningAsync(
                    workspaceRoot,
                    binding.SessionId,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }

            await _journal.WriteStatusAsync(
                workspaceRoot,
                binding.SessionId,
                binding.TurnId,
                ConversationControlState.Ready,
                canSend: true,
                error: null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var envelope = pending[0];
        var suppressedDuplicate = await _journal.TrySuppressRecentDuplicateAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        if (suppressedDuplicate is not null)
        {
            await _journal.WriteStatusAsync(
                workspaceRoot,
                binding.SessionId,
                binding.TurnId,
                ConversationControlState.Ready,
                canSend: true,
                error: null,
                cancellationToken).ConfigureAwait(false);
            Wake();
            return;
        }

        if (envelope.Record.State == ConversationDeliveryState.Failed)
        {
            // ReadPendingAsync exposes only the legacy pre-turn active-writer failure here. Normalize
            // it immediately so the browser no longer presents recoverable work as permanently failed.
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Queued,
                turnId: null,
                "Codex Desktop still owns this task. CAVE will retry when the task writer is released.",
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
        }

        if (envelope.Record.State == ConversationDeliveryState.Running)
        {
            const string error = "The previous CAVE host stopped after Codex may have accepted this turn. "
                + "Delivery was not replayed; inspect the task before submitting it again.";
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Failed,
                envelope.Record.TurnId,
                error,
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
            await WriteFailedStatusAsync(workspaceRoot, binding, envelope.Record.Error!, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (binding.IsActive)
        {
            await _journal.WriteStatusAsync(
                workspaceRoot,
                binding.SessionId,
                binding.TurnId,
                ConversationControlState.Queued,
                canSend: false,
                "Waiting for the desktop-owned Codex turn to finish.",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!envelope.Record.SessionId.Equals(binding.SessionId, StringComparison.Ordinal))
        {
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Failed,
                turnId: null,
                "The workspace switched to a different Codex task before delivery.",
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
            await WriteFailedStatusAsync(workspaceRoot, binding, envelope.Record.Error!, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await RunDeliveryAsync(workspaceRoot, binding, envelope, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunDeliveryAsync(
        string workspaceRoot,
        CodexTaskBinding binding,
        ConversationControlJournal.DeliveryEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var claimed = await _journal.TryClaimAsync(envelope, _instanceId, cancellationToken)
            .ConfigureAwait(false);
        if (claimed is null)
        {
            // The owner Stop hook claimed the queued message after this scan read it.
            // It is already entering the exact desktop-owned task and must not be replayed.
            return;
        }

        envelope = claimed;
        await _journal.WriteStatusAsync(
            workspaceRoot,
            binding.SessionId,
            envelope.Record.TurnId ?? binding.TurnId,
            ConversationControlState.Running,
            canSend: false,
            error: null,
            cancellationToken).ConfigureAwait(false);

        try
        {
            var turnId = await _turnRunner.RunAsync(
                workspaceRoot,
                binding.SessionId,
                envelope.Record.MessageId,
                envelope.Record.Text,
                async (startedTurnId, token) =>
                {
                    envelope = await _journal.UpdateAsync(
                        envelope,
                        ConversationDeliveryState.Running,
                        startedTurnId,
                        error: null,
                        _instanceId,
                        token).ConfigureAwait(false);
                    await _journal.WriteStatusAsync(
                        workspaceRoot,
                        binding.SessionId,
                        startedTurnId,
                        ConversationControlState.Running,
                        canSend: false,
                        error: null,
                        token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Completed,
                turnId,
                error: null,
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
            await _journal.WriteStatusAsync(
                workspaceRoot,
                binding.SessionId,
                turnId,
                ConversationControlState.Ready,
                canSend: true,
                error: null,
                cancellationToken).ConfigureAwait(false);
            Wake();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Queued,
                envelope.Record.TurnId,
                "The CAVE host stopped during delivery; this message will be recovered on restart.",
                bridgeInstanceId: null,
                recoveryTimeout.Token).ConfigureAwait(false);
            throw;
        }
        catch (CodexTaskWriterBusyException exception)
        {
            // App Server rejected thread/resume before turn/start, so no prompt was accepted and
            // returning the envelope to the durable queue cannot duplicate a Codex turn.
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Queued,
                turnId: null,
                exception.Message,
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
            await _journal.WriteStatusAsync(
                workspaceRoot,
                binding.SessionId,
                binding.TurnId,
                ConversationControlState.Queued,
                canSend: false,
                exception.Message,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError(
                "CAVE could not deliver browser message {0} to Codex task {1}: {2}",
                envelope.Record.MessageId,
                binding.SessionId,
                exception);
            envelope = await _journal.UpdateAsync(
                envelope,
                ConversationDeliveryState.Failed,
                envelope.Record.TurnId,
                exception.Message,
                bridgeInstanceId: null,
                cancellationToken).ConfigureAwait(false);
            await WriteFailedStatusAsync(workspaceRoot, binding, exception.Message, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task WriteFailedStatusAsync(
        string workspaceRoot,
        CodexTaskBinding binding,
        string error,
        CancellationToken cancellationToken) =>
        _journal.WriteStatusAsync(
            workspaceRoot,
            binding.SessionId,
            binding.TurnId,
            ConversationControlState.Failed,
            canSend: true,
            error,
            cancellationToken);

    private void Wake()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    private static ConversationDelivery ToDelivery(ConversationControlJournal.DeliveryRecord record) => new(
        record.MessageId,
        record.SessionId,
        record.TurnId,
        record.State,
        record.QueuedAtUtc,
        record.UpdatedAtUtc,
        record.Error);

    /// <inheritdoc />
    public void Dispose()
    {
        _wake.Dispose();
    }
}
