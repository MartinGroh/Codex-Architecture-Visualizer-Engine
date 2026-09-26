using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Conversation;

/// <summary>
/// Reads opt-in public conversation hook records and atomically controls workspace sharing.
/// </summary>
public sealed class FileConversationStore : IConversationStore
{
    /// <summary>Gets the workspace-relative directory containing conversation hook records.</summary>
    public const string ConversationDirectory = ".cave/conversation/inbox";

    /// <summary>Gets the workspace-relative opt-in settings file.</summary>
    public const string SettingsPath = ".cave/conversation/settings.json";

    private const int MaxProjectedMessages = 512;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <inheritdoc />
    public async Task<ConversationOverlay> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        var control = await ConversationControlJournal.ReadAsync(canonicalRoot, cancellationToken)
            .ConfigureAwait(false);
        var settings = await ReadSettingsAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled)
        {
            return settings.Error is null
                ? ConversationOverlay.Disabled with { Control = control }
                : new ConversationOverlay(
                    SharingEnabled: false,
                    ConversationSourceStatus.Degraded,
                    [],
                    control,
                    settings.Error);
        }

        var directory = Path.Combine(canonicalRoot, ".cave", "conversation", "inbox");
        if (!Directory.Exists(directory))
        {
            return new ConversationOverlay(
                true,
                ConversationSourceStatus.Ready,
                [],
                control,
                settings.Error);
        }

        var messages = new List<ConversationMessage>();
        var errors = new List<string>();
        if (settings.Error is not null)
        {
            errors.Add(settings.Error);
        }

        var paths = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderDescending(StringComparer.Ordinal)
            .Take(MaxProjectedMessages)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var item = await JsonSerializer.DeserializeAsync<ConversationEvent>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (item is null || item.SchemaVersion != 1 || string.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add($"{Path.GetFileName(path)} has an unsupported conversation schema.");
                    continue;
                }

                if (EphemeralConversationSession.IsMarked(canonicalRoot, item.SessionId))
                {
                    continue;
                }

                messages.Add(new ConversationMessage(
                    item.EventId,
                    item.SessionId,
                    item.TurnId,
                    item.AgentId,
                    item.AgentType,
                    item.IsSubagent,
                    item.Role,
                    item.Kind,
                    item.Text,
                    item.IsTruncated,
                    item.IsStreaming,
                    item.OccurredAtUtc));
            }
            catch (JsonException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
            catch (IOException exception)
            {
                if (File.Exists(path))
                {
                    errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
                }
            }
        }

        var projectedMessages = messages
            .GroupBy(
                item => $"{item.SessionId}\0{item.TurnId}\0{item.Role}\0{item.Kind}\0{item.Text}",
                StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(item => item.IsStreaming)
                .ThenByDescending(item => item.OccurredAtUtc)
                .First())
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.EventId, StringComparer.Ordinal)
            .ToArray();

        return new ConversationOverlay(
            SharingEnabled: true,
            errors.Count == 0 ? ConversationSourceStatus.Ready : ConversationSourceStatus.Degraded,
            projectedMessages,
            control,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    /// <inheritdoc />
    public async Task SetSharingAsync(
        string workspaceRoot,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{canonicalRoot}' does not exist.");
        }

        var directory = Path.Combine(canonicalRoot, ".cave", "conversation");
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, "settings.json");
        var temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(new ConversationSettings(1, enabled), JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, finalPath, overwrite: true);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        string workspaceRoot,
        ConversationMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{canonicalRoot}' does not exist.");
        }

        var directory = Path.Combine(canonicalRoot, ".cave", "conversation", "inbox");
        Directory.CreateDirectory(directory);
        var stableId = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(message.EventId))).ToLowerInvariant()[..24];
        var finalPath = Path.Combine(
            directory,
            $"{message.OccurredAtUtc:yyyyMMddHHmmssfffffff}-bridge-{stableId}.json");
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        var record = new ConversationEvent(
            SchemaVersion: 1,
            message.EventId,
            message.OccurredAtUtc,
            message.SessionId,
            message.TurnId,
            message.AgentId,
            message.AgentType,
            message.IsSubagent,
            message.Role,
            message.Kind,
            message.Text,
            message.IsTruncated,
            message.IsStreaming);
        try
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<SettingsRead> ReadSettingsAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspaceRoot, ".cave", "conversation", "settings.json");
        if (!File.Exists(path))
        {
            return new SettingsRead(false, null);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var settings = await JsonSerializer.DeserializeAsync<ConversationSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return settings is { SchemaVersion: 1 }
                ? new SettingsRead(settings.ConversationSharingEnabled, null)
                : new SettingsRead(false, "Conversation settings use an unsupported schema.");
        }
        catch (JsonException exception)
        {
            return new SettingsRead(false, $"Conversation settings are invalid: {exception.Message}");
        }
        catch (IOException exception)
        {
            return new SettingsRead(false, $"Conversation settings are unavailable: {exception.Message}");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record ConversationSettings(
        int SchemaVersion,
        bool ConversationSharingEnabled);

    private sealed record ConversationEvent(
        int SchemaVersion,
        string EventId,
        DateTimeOffset OccurredAtUtc,
        string? SessionId,
        string? TurnId,
        string? AgentId,
        string? AgentType,
        bool IsSubagent,
        ConversationRole Role,
        ConversationMessageKind Kind,
        string Text,
        bool IsTruncated,
        bool IsStreaming = false);

    private sealed record SettingsRead(bool Enabled, string? Error);
}
