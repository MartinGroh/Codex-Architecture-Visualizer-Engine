using System.Text.Json;
using Cave.Application;

namespace Cave.Infrastructure.Activity;

/// <summary>Owns atomic, cross-process-locked machine-local telemetry records keyed by a native identity hash.</summary>
public sealed class FileAgentTelemetryStore(string rootPath, TimeProvider timeProvider) : IAgentTelemetryStore, IDisposable
{
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private AgentTelemetryState? _cached;
    private DateTimeOffset _readAt;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Gets the canonical per-user statistics directory.</summary>
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAVE", "statistics", "v1");

    /// <inheritdoc />
    public void Dispose() => _readGate.Dispose();

    /// <inheritdoc />
    public async Task<AgentTelemetryState> ReadAsync(CancellationToken cancellationToken)
    {
        await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_cached is not null && now >= _readAt && now - _readAt < TimeSpan.FromSeconds(3)) return _cached;
            var records = new List<AgentTelemetryRecord>();
            var partial = false;
            try
            {
                if (Directory.Exists(rootPath))
                {
                    var paths = Directory.EnumerateFiles(rootPath, "*.json").Order(StringComparer.Ordinal).Take(10_001).ToArray();
                    partial = paths.Length > 10_000;
                    foreach (var path in paths.Take(10_000))
                    {
                        try
                        {
                            var record = await ReadRecordAsync(path, cancellationToken).ConfigureAwait(false);
                            if (record is not null) records.Add(record);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                        { partial = true; }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { partial = true; }
            _readAt = now;
            return _cached = new(records, partial ? "Some retained statistics records could not be read; totals have partial coverage." : null);
        }
        finally { _readGate.Release(); }
    }

    /// <inheritdoc />
    public async Task ObserveAsync(IReadOnlyList<AgentTelemetryObservation> observations, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootPath);
        foreach (var observation in observations)
        {
            var key = AgentDisplayNames.PublicId(observation.Sample.ThreadId);
            var destination = Path.Combine(rootPath, key + ".json");
            await using var gate = await LockAsync(Path.Combine(rootPath, key + ".lock"), cancellationToken).ConfigureAwait(false);
            // A malformed existing record must not be treated as an empty lifetime history.
            var prior = await ReadRecordAsync(destination, cancellationToken).ConfigureAwait(false);
            var next = AgentTelemetryAccounting.Apply(prior, observation);
            Validate(next, key);
            var temporary = Path.Combine(rootPath, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, new Document(1, next), Options, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, destination, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        _cached = null;
    }

    private static async Task<AgentTelemetryRecord?> ReadRecordAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > 1_048_576) throw new InvalidDataException("Telemetry record exceeds its size limit.");
        var document = await JsonSerializer.DeserializeAsync<Document>(stream, Options, cancellationToken).ConfigureAwait(false);
        if (document is not { SchemaVersion: 1, Record: not null }) throw new InvalidDataException("Unsupported telemetry record.");
        Validate(document.Record, Path.GetFileNameWithoutExtension(path));
        return document.Record;
    }

    private static void Validate(AgentTelemetryRecord record, string key)
    {
        bool SafeText(string? value, int limit) => value is null || value.Length <= limit && !value.Any(char.IsControl);
        if (!AgentTelemetryIdentity.IsNative(record.ThreadId)
            || record.ParentThreadId is not null && !AgentTelemetryIdentity.IsNative(record.ParentThreadId)
            || AgentDisplayNames.PublicId(record.ThreadId) != key || record.WorkspaceIds is null
            || record.WorkspaceIds.Count > 256 || record.WorkspaceIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128)
            || record.ObservedTime is null || record.ObservedTime.Count > 512
            || record.ObservedTime.Any(item => item is null || item.Milliseconds < 0 || !SafeText(item.Model, 128) || !SafeText(item.ReasoningEffort, 32))
            || record.Usage?.Count > 64 || record.Usage?.Any(item => item is null || item.InputTokens < 0 || item.CachedInputTokens < 0
                || item.OutputTokens < 0 || item.TotalTokens < 0 || !SafeText(item.Model, 128) || !SafeText(item.ReasoningEffort, 32)) == true
            || !SafeText(record.Configuration?.Model, 128) || !SafeText(record.Configuration?.ReasoningEffort, 32)
            || record.CompletedDurationMs < 0 || record.CompletedTurnCount < 0
            || record.LastSampleAtUtc == default || record.FirstSeenAtUtc > record.LastSampleAtUtc)
            throw new InvalidDataException("Invalid telemetry record.");
        try
        {
            if (record.ObservedTime.Sum(item => item.Milliseconds) > (record.LastSampleAtUtc - record.FirstSeenAtUtc).TotalMilliseconds)
                throw new InvalidDataException("Observed time exceeds the retained observation interval.");
        }
        catch (OverflowException exception) { throw new InvalidDataException("Observed time exceeds the retained observation interval.", exception); }
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 40) { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
        }
    }

    private sealed record Document(int SchemaVersion, AgentTelemetryRecord Record);
}
