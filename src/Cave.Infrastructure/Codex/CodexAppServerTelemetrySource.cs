using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Codex;

/// <summary>Reads only exact thread metadata, nullable usage groups and item-free turn durations.</summary>
public sealed class CodexAppServerTelemetrySource(TimeProvider timeProvider, string? codexCommand = null)
    : IAgentTelemetrySource, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, AgentTelemetrySample> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTelemetrySample>> ReadAsync(
        IReadOnlyList<string> threadIds, CancellationToken cancellationToken)
    {
        if (threadIds.Count > 32 || threadIds.Any(id => !AgentTelemetryIdentity.IsNative(id)))
            throw new ArgumentException("Telemetry requires at most 32 bounded native identities.", nameof(threadIds));
        var ids = threadIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            foreach (var key in _cache.Where(pair => now < pair.Value.RetrievedAtUtc
                         || now - pair.Value.RetrievedAtUtc >= TimeSpan.FromSeconds(15)).Select(pair => pair.Key).ToArray())
                _cache.Remove(key);
            var missing = ids.Where(id => !_cache.ContainsKey(id)).ToArray();
            if (missing.Length > 0)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                try
                {
                    await ReadBatchAsync(missing, now, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                catch (Exception exception) when (IsBoundaryFailure(exception)) { }
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var id in missing)
                    _cache.TryAdd(id, new(id, null, null, null, null, 0, false, now,
                        "Native agent metadata is unavailable."));
            }
            if (_cache.Count > 128)
                foreach (var key in _cache.OrderBy(pair => pair.Value.RetrievedAtUtc)
                             .Select(pair => pair.Key).Where(key => !ids.Contains(key, StringComparer.Ordinal))
                             .Take(_cache.Count - 128).ToArray()) _cache.Remove(key);
            return ids.Select(id => _cache[id]).ToArray();
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task ReadBatchAsync(string[] ids, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(codexCommand) };
        if (!process.Start()) throw new InvalidOperationException("The metadata process could not start.");
        var drain = DrainAsync(process.StandardError, cancellationToken);
        try
        {
            var reader = new CodexAppServerJsonReader(process.StandardOutput);
            var requestId = 0;
            async Task<JsonDocument> Request(string method, object parameters)
            {
                var id = ++requestId;
                await CodexAppServerProcess.WriteAsync(process, new { method, id, @params = parameters }, cancellationToken)
                    .ConfigureAwait(false);
                var document = await reader.ReadResponseAsync(id, cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("error", out _))
                {
                    document.Dispose();
                    throw new InvalidOperationException("The native metadata request was rejected.");
                }
                return document;
            }

            using var initialize = await Request("initialize", new
            {
                clientInfo = new { name = "cave", version = "0.3.0" },
                capabilities = new { experimentalApi = true },
            }).ConfigureAwait(false);
            await CodexAppServerProcess.WriteAsync(process,
                new { method = "initialized", @params = new { } }, cancellationToken).ConfigureAwait(false);

            foreach (var id in ids)
            {
                try
                {
                    using var metadata = await Request("thread/read", new { threadId = id, includeTurns = false }).ConfigureAwait(false);
                    var thread = metadata.RootElement.GetProperty("result").GetProperty("thread");
                    if (thread.GetProperty("id").GetString() != id
                        || thread.TryGetProperty("ephemeral", out var ephemeral) && ephemeral.GetBoolean())
                        throw new InvalidOperationException("The native thread identity did not match.");
                    var configuration = new AgentModelConfiguration(Text(thread, "model", 128), Text(thread, "reasoningEffort", 32));
                    var parent = Text(thread, "parentThreadId", 128);
                    if (parent is not null && !AgentTelemetryIdentity.IsNative(parent))
                        throw new InvalidOperationException("The native parent identity is malformed.");
                    // Keep independent metadata available if a later usage/duration read times out.
                    _cache[id] = new(id, parent, configuration, null, null, 0, false, now,
                        "Thread usage and duration have not been read.");
                    IReadOnlyList<AgentTokenUsage>? usage = null;
                    long? duration = null;
                    var turnCount = 0;
                    var complete = false;
                    var errors = new List<string>();
                    try
                    {
                        using var response = await Request("account/usage/read", new { threadId = id }).ConfigureAwait(false);
                        var result = response.RootElement.GetProperty("result");
                        if (result.TryGetProperty("threadUsage", out var native) && native.ValueKind != JsonValueKind.Null)
                        {
                            if (native.GetProperty("threadId").GetString() != id)
                                throw new InvalidOperationException("Foreign usage identity.");
                            var groups = native.GetProperty("groups");
                            if (groups.GetArrayLength() > 64) throw new InvalidOperationException("Too many usage groups.");
                            usage = groups.EnumerateArray().Select(group => new AgentTokenUsage(
                                Text(group, "model", 128), Text(group, "reasoningEffort", 32),
                                Counter(group, "inputTokens"), Counter(group, "cachedInputTokens"),
                                Counter(group, "outputTokens"), Counter(group, "totalTokens"))).ToArray();
                        }
                        if (usage is null) errors.Add("Thread token breakdown is unavailable.");
                    }
                    catch (Exception exception) when (IsBoundaryFailure(exception))
                    { errors.Add("Thread token breakdown is unavailable."); }

                    _cache[id] = new(id, parent, configuration, usage, null, 0, false, now,
                        "Recorded turn duration is unavailable.");

                    try
                    {
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        var cursors = new HashSet<string>(StringComparer.Ordinal);
                        string? cursor = null;
                        long total = 0;
                        complete = true;
                        for (var page = 0; page < 4; page++)
                        {
                            using var response = await Request("thread/turns/list", new
                            { threadId = id, cursor, limit = 50, itemsView = "notLoaded" }).ConfigureAwait(false);
                            var result = response.RootElement.GetProperty("result");
                            var turns = result.GetProperty("data");
                            if (turns.GetArrayLength() > 50) throw new InvalidOperationException("Turn page exceeded its limit.");
                            foreach (var turn in turns.EnumerateArray())
                            {
                                var turnId = Text(turn, "id", 128) ?? throw new InvalidOperationException("Missing turn identity.");
                                if (!seen.Add(turnId) || turn.GetProperty("status").GetString() == "inProgress") continue;
                                var milliseconds = Counter(turn, "durationMs");
                                if (milliseconds is null) { complete = false; continue; }
                                total = checked(total + milliseconds.Value);
                                turnCount++;
                            }
                            cursor = Text(result, "nextCursor", 2048);
                            if (cursor is null) break;
                            if (!cursors.Add(cursor) || page == 3) { complete = false; break; }
                        }
                        duration = total;
                        if (!complete) errors.Add("Recorded turn duration has partial coverage.");
                    }
                    catch (Exception exception) when (IsBoundaryFailure(exception))
                    { complete = false; errors.Add("Recorded turn duration is unavailable."); }
                    _cache[id] = new(id, parent, configuration, usage, duration, turnCount, complete, now,
                        errors.Count == 0 ? null : string.Join(" ", errors));
                }
                catch (Exception exception) when (IsBoundaryFailure(exception))
                { _cache[id] = new(id, null, null, null, null, 0, false, now, "Native agent metadata is unavailable."); }
            }
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            try { await drain.ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
        }
    }

    private static bool IsBoundaryFailure(Exception exception) => exception is IOException or JsonException
        or InvalidOperationException or Win32Exception or ArgumentException or FormatException or KeyNotFoundException or OverflowException;

    private static string? Text(JsonElement owner, string name, int limit)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > limit || text.Any(char.IsControl)) throw new InvalidOperationException("Invalid native metadata text.");
        return text;
    }

    private static long? Counter(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var number = value.GetInt64();
        return number >= 0 ? number : throw new InvalidOperationException("Negative native usage.");
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0) { }
    }
}
