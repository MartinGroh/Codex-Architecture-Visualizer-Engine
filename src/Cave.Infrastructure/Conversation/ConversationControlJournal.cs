using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Domain;

namespace Cave.Infrastructure.Conversation;

/// <summary>
/// Owns the durable workspace-local browser-message queue and bridge status contract.
/// </summary>
internal sealed class ConversationControlJournal(TimeProvider timeProvider)
{
    internal const string ControlDirectory = ".cave/conversation/control";
    internal const string OutboxDirectory = ".cave/conversation/control/outbox";
    private const string StatusFileName = "status.json";
    private const string QueueLockFileName = "queue.lock";
    private const string OwnerHookInstanceId = "codex-owner-hook";
    private static readonly TimeSpan DuplicateReplayWindow = TimeSpan.FromMinutes(2);
    private const int MaxProjectedDeliveries = 32;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Reads the durable bridge state projected for the browser.</summary>
    internal static async Task<ConversationControl> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        var statusPath = Path.Combine(canonicalRoot, ".cave", "conversation", "control", StatusFileName);
        var status = await ReadRecordAsync<BridgeStatusRecord>(statusPath, cancellationToken).ConfigureAwait(false);
        var deliveries = new List<ConversationDelivery>();
        var outbox = GetOutboxDirectory(canonicalRoot);
        if (Directory.Exists(outbox))
        {
            foreach (var path in Directory.EnumerateFiles(outbox, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderDescending(StringComparer.Ordinal)
                         .Take(MaxProjectedDeliveries)
                         .Order(StringComparer.Ordinal))
            {
                var record = await ReadRecordAsync<DeliveryRecord>(path, cancellationToken).ConfigureAwait(false);
                if (record is { SchemaVersion: 1 })
                {
                    deliveries.Add(ToDelivery(record));
                }
            }
        }

        if (status is not { SchemaVersion: 1 })
        {
            return ConversationControl.Unavailable with { Deliveries = deliveries };
        }

        return new ConversationControl(
            status.SessionId,
            status.TurnId,
            status.State,
            status.CanSend,
            deliveries,
            status.Error);
    }

    /// <summary>Persists a new queued browser message.</summary>
    internal async Task<DeliveryEnvelope> QueueSingleFlightAsync(
        string workspaceRoot,
        string sessionId,
        string text,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        await using var queueLock = await AcquireQueueLockAsync(canonicalRoot, cancellationToken)
            .ConfigureAwait(false);
        var existing = await FindOpenCoreAsync(canonicalRoot, sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Record.Text.Equals(text, StringComparison.Ordinal))
            {
                return existing;
            }

            throw new InvalidOperationException(
                "Another browser message is already waiting for this Codex task. "
                + "Wait for it to be delivered before sending another message.");
        }

        var recentDuplicate = await FindRecentCompletedCoreAsync(
            canonicalRoot,
            sessionId,
            text,
            timeProvider.GetUtcNow() - DuplicateReplayWindow,
            cancellationToken).ConfigureAwait(false);
        if (recentDuplicate is not null)
        {
            return recentDuplicate;
        }

        var now = timeProvider.GetUtcNow();
        var messageId = Guid.NewGuid().ToString("N");
        var record = new DeliveryRecord(
            SchemaVersion: 1,
            MessageId: messageId,
            SessionId: sessionId,
            TurnId: null,
            Text: text,
            ConversationDeliveryState.Queued,
            QueuedAtUtc: now,
            UpdatedAtUtc: now,
            Error: null,
            BridgeInstanceId: null);
        var directory = GetOutboxDirectory(canonicalRoot);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{now:yyyyMMddHHmmssfffffff}-{messageId}.json");
        await WriteRecordAsync(path, record, cancellationToken).ConfigureAwait(false);
        return new DeliveryEnvelope(path, record);
    }

    /// <summary>Finds the oldest open delivery bound to one exact task.</summary>
    internal static async Task<DeliveryEnvelope?> FindOpenAsync(
        string workspaceRoot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        await using var queueLock = await AcquireQueueLockAsync(canonicalRoot, cancellationToken)
            .ConfigureAwait(false);
        return await FindOpenCoreAsync(canonicalRoot, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DeliveryEnvelope?> FindOpenCoreAsync(
        string canonicalRoot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var directory = GetOutboxDirectory(canonicalRoot);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync<DeliveryRecord>(path, cancellationToken).ConfigureAwait(false);
            if (record is { SchemaVersion: 1 }
                && record.SessionId.Equals(sessionId, StringComparison.Ordinal)
                && record.State is ConversationDeliveryState.Queued or ConversationDeliveryState.Running)
            {
                return new DeliveryEnvelope(path, record);
            }
        }

        return null;
    }

    private static async Task<DeliveryEnvelope?> FindRecentCompletedCoreAsync(
        string canonicalRoot,
        string sessionId,
        string text,
        DateTimeOffset updatedAfter,
        CancellationToken cancellationToken)
    {
        var directory = GetOutboxDirectory(canonicalRoot);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderDescending(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync<DeliveryRecord>(path, cancellationToken).ConfigureAwait(false);
            if (record is { SchemaVersion: 1, State: ConversationDeliveryState.Completed }
                && record.SessionId.Equals(sessionId, StringComparison.Ordinal)
                && record.Text.Equals(text, StringComparison.Ordinal)
                && record.UpdatedAtUtc >= updatedAfter)
            {
                return new DeliveryEnvelope(path, record);
            }
        }

        return null;
    }

    /// <summary>Completes an immediate same-text replay left by an earlier bridge build.</summary>
    internal async Task<DeliveryEnvelope?> TrySuppressRecentDuplicateAsync(
        DeliveryEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = ResolveWorkspaceRoot(envelope.Path);
        await using var queueLock = await AcquireQueueLockAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var current = await ReadRecordAsync<DeliveryRecord>(envelope.Path, cancellationToken)
            .ConfigureAwait(false);
        if (current is not { SchemaVersion: 1, State: ConversationDeliveryState.Queued })
        {
            return null;
        }

        var duplicate = await FindRecentCompletedCoreAsync(
            workspaceRoot,
            current.SessionId,
            current.Text,
            current.QueuedAtUtc - DuplicateReplayWindow,
            cancellationToken).ConfigureAwait(false);
        if (duplicate is null || duplicate.Path.Equals(envelope.Path, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var completed = current with
        {
            State = ConversationDeliveryState.Completed,
            TurnId = duplicate.Record.TurnId,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Error = null,
            BridgeInstanceId = "recent-duplicate-suppression",
        };
        await WriteRecordAsync(envelope.Path, completed, cancellationToken).ConfigureAwait(false);
        return new DeliveryEnvelope(envelope.Path, completed);
    }

    /// <summary>Reports whether an owner-hook continuation is still producing its response.</summary>
    internal static async Task<bool> IsOwnerHookDeliveryRunningAsync(
        string workspaceRoot,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        var statusPath = Path.Combine(canonicalRoot, ".cave", "conversation", "control", StatusFileName);
        var status = await ReadRecordAsync<BridgeStatusRecord>(statusPath, cancellationToken)
            .ConfigureAwait(false);
        return status is
        {
            SchemaVersion: 1,
            State: ConversationControlState.Running,
            Owner: OwnerHookInstanceId,
        } && string.Equals(status.SessionId, sessionId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Atomically claims a queued delivery so the owner Stop hook and App Server runner cannot
    /// both submit the same browser message.
    /// </summary>
    internal async Task<DeliveryEnvelope?> TryClaimAsync(
        DeliveryEnvelope envelope,
        string bridgeInstanceId,
        CancellationToken cancellationToken)
    {
        var workspaceRoot = ResolveWorkspaceRoot(envelope.Path);
        await using var queueLock = await AcquireQueueLockAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var current = await ReadRecordAsync<DeliveryRecord>(envelope.Path, cancellationToken)
            .ConfigureAwait(false);
        if (current is not { SchemaVersion: 1, State: ConversationDeliveryState.Queued })
        {
            return null;
        }

        var claimed = current with
        {
            State = ConversationDeliveryState.Running,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Error = null,
            BridgeInstanceId = bridgeInstanceId,
        };
        await WriteRecordAsync(envelope.Path, claimed, cancellationToken).ConfigureAwait(false);
        return new DeliveryEnvelope(envelope.Path, claimed);
    }

    /// <summary>Reads queued deliveries and recovers work owned by an earlier bridge process.</summary>
    internal static async Task<IReadOnlyList<DeliveryEnvelope>> ReadPendingAsync(
        string workspaceRoot,
        string bridgeInstanceId,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        var directory = GetOutboxDirectory(canonicalRoot);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var pending = new List<DeliveryEnvelope>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await ReadRecordAsync<DeliveryRecord>(path, cancellationToken).ConfigureAwait(false);
            if (record is not { SchemaVersion: 1 })
            {
                continue;
            }

            if (record.State == ConversationDeliveryState.Queued
                || (record.State == ConversationDeliveryState.Running
                    && !string.Equals(record.BridgeInstanceId, OwnerHookInstanceId, StringComparison.Ordinal)
                    && !string.Equals(record.BridgeInstanceId, bridgeInstanceId, StringComparison.Ordinal))
                || IsLegacyWriterBusyFailure(record))
            {
                pending.Add(new DeliveryEnvelope(path, record));
            }
        }

        return pending;
    }

    private static bool IsLegacyWriterBusyFailure(DeliveryRecord record) =>
        record.State == ConversationDeliveryState.Failed
        && record.TurnId is null
        && record.Error?.Contains("already has an active writer", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Updates one durable delivery atomically.</summary>
    internal async Task<DeliveryEnvelope> UpdateAsync(
        DeliveryEnvelope envelope,
        ConversationDeliveryState state,
        string? turnId,
        string? error,
        string? bridgeInstanceId,
        CancellationToken cancellationToken)
    {
        var updated = envelope.Record with
        {
            State = state,
            TurnId = turnId ?? envelope.Record.TurnId,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            Error = error,
            BridgeInstanceId = bridgeInstanceId,
        };
        await WriteRecordAsync(envelope.Path, updated, cancellationToken).ConfigureAwait(false);
        return new DeliveryEnvelope(envelope.Path, updated);
    }

    /// <summary>Updates the browser-facing bridge status atomically.</summary>
    internal async Task WriteStatusAsync(
        string workspaceRoot,
        string? sessionId,
        string? turnId,
        ConversationControlState state,
        bool canSend,
        string? error,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeRoot(workspaceRoot);
        var path = Path.Combine(canonicalRoot, ".cave", "conversation", "control", StatusFileName);
        var current = await ReadRecordAsync<BridgeStatusRecord>(path, cancellationToken).ConfigureAwait(false);
        if (current is { SchemaVersion: 1 }
            && string.Equals(current.SessionId, sessionId, StringComparison.Ordinal)
            && string.Equals(current.TurnId, turnId, StringComparison.Ordinal)
            && current.State == state
            && current.CanSend == canSend
            && current.Owner is null
            && string.Equals(current.Error, error, StringComparison.Ordinal))
        {
            return;
        }

        var record = new BridgeStatusRecord(
            SchemaVersion: 1,
            SessionId: sessionId,
            TurnId: turnId,
            State: state,
            CanSend: canSend,
            Error: error,
            Owner: null,
            UpdatedAtUtc: timeProvider.GetUtcNow());
        await WriteRecordAsync(path, record, cancellationToken).ConfigureAwait(false);
    }

    private static ConversationDelivery ToDelivery(DeliveryRecord record) => new(
        record.MessageId,
        record.SessionId,
        record.TurnId,
        record.State,
        record.QueuedAtUtc,
        record.UpdatedAtUtc,
        record.Error);

    private static string GetOutboxDirectory(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".cave", "conversation", "control", "outbox");

    private static string ResolveWorkspaceRoot(string deliveryPath)
    {
        var outbox = Directory.GetParent(deliveryPath)
            ?? throw new InvalidOperationException($"Cannot resolve the outbox for '{deliveryPath}'.");
        var control = outbox.Parent
            ?? throw new InvalidOperationException($"Cannot resolve the control directory for '{deliveryPath}'.");
        var conversation = control.Parent
            ?? throw new InvalidOperationException($"Cannot resolve the conversation directory for '{deliveryPath}'.");
        var cave = conversation.Parent
            ?? throw new InvalidOperationException($"Cannot resolve the CAVE directory for '{deliveryPath}'.");
        return cave.Parent?.FullName
            ?? throw new InvalidOperationException($"Cannot resolve the workspace root for '{deliveryPath}'.");
    }

    private static async Task<FileStream> AcquireQueueLockAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var controlDirectory = Path.Combine(workspaceRoot, ".cave", "conversation", "control");
        Directory.CreateDirectory(controlDirectory);
        var path = Path.Combine(controlDirectory, QueueLockFileName);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? stream = null;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
                if (stream.Length == 0)
                {
                    stream.WriteByte(0);
                    stream.Flush(flushToDisk: true);
                }

                if (OperatingSystem.IsMacOS())
                {
                    throw new PlatformNotSupportedException(
                        "The CAVE cross-process conversation queue lock is not supported on macOS.");
                }

                stream.Lock(0, 1);
                return stream;
            }
            catch (IOException)
            {
                if (stream is not null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string CanonicalizeRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }

    private static async Task<T?> ReadRecordAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            return default;
        }
    }

    private static async Task WriteRecordAsync<T>(
        string path,
        T record,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Cannot resolve the journal directory for '{path}'.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    internal sealed record DeliveryEnvelope(string Path, DeliveryRecord Record);

    internal sealed record DeliveryRecord(
        int SchemaVersion,
        string MessageId,
        string SessionId,
        string? TurnId,
        string Text,
        ConversationDeliveryState State,
        DateTimeOffset QueuedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? Error,
        string? BridgeInstanceId);

    private sealed record BridgeStatusRecord(
        int SchemaVersion,
        string? SessionId,
        string? TurnId,
        ConversationControlState State,
        bool CanSend,
        string? Error,
        string? Owner,
        DateTimeOffset UpdatedAtUtc);
}
