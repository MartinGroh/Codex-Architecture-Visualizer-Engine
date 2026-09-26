using System.Diagnostics;
using System.Text.Json;
using Cave.Application;

namespace Cave.Infrastructure.Codex;

/// <summary>
/// Reads account usage through the local Codex App Server JSONL protocol.
/// </summary>
public sealed class CodexAppServerUsageProvider : ICodexUsageProvider, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly TimeProvider _timeProvider;
    private readonly string? _codexCommand;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CodexUsageSnapshot? _cached;

    /// <summary>
    /// Initializes the provider with the authoritative clock and an optional configured Codex command.
    /// </summary>
    /// <param name="timeProvider">The authoritative clock.</param>
    /// <param name="codexCommand">An explicit Codex executable or command path, or <see langword="null"/> for PATH resolution.</param>
    public CodexAppServerUsageProvider(TimeProvider timeProvider, string? codexCommand = null)
    {
        _timeProvider = timeProvider;
        _codexCommand = string.IsNullOrWhiteSpace(codexCommand) ? null : codexCommand.Trim();
    }

    /// <inheritdoc />
    public async Task<CodexUsageSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_cached is { } cached && now - cached.RetrievedAtUtc < CacheDuration)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (_cached is { } rechecked && now - rechecked.RetrievedAtUtc < CacheDuration)
            {
                return rechecked;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                _cached = await ReadFromAppServerAsync(now, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _cached = Unavailable(now, "Codex account usage timed out.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _cached = Unavailable(now, $"Codex account usage is unavailable: {exception.Message}");
            }

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<CodexUsageSnapshot> ReadFromAppServerAsync(
        DateTimeOffset retrievedAt,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(_codexCommand) };
        if (!process.Start())
        {
            return Unavailable(retrievedAt, "The Codex App Server process could not be started.");
        }

        _ = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "initialize",
                    id = 1,
                    @params = new { clientInfo = new { name = "cave", version = "0.3.0" } },
                },
                cancellationToken).ConfigureAwait(false);
            using var initialize = await ReadResponseAsync(process, 1, cancellationToken).ConfigureAwait(false);
            ThrowIfError(initialize.RootElement, "Codex App Server initialization failed");

            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "initialized", @params = new { } },
                cancellationToken).ConfigureAwait(false);
            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "account/usage/read", id = 2, @params = new { } },
                cancellationToken).ConfigureAwait(false);
            using var usage = await ReadResponseAsync(process, 2, cancellationToken).ConfigureAwait(false);
            ThrowIfError(usage.RootElement, "Codex account usage request failed");

            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "account/rateLimits/read", id = 3, @params = new { } },
                cancellationToken).ConfigureAwait(false);
            using var limits = await ReadResponseAsync(process, 3, cancellationToken).ConfigureAwait(false);
            ThrowIfError(limits.RootElement, "Codex rate-limit request failed");

            var result = usage.RootElement.GetProperty("result");
            var summary = result.GetProperty("summary");
            var daily = result.TryGetProperty("dailyUsageBuckets", out var buckets)
                && buckets.ValueKind == JsonValueKind.Array
                    ? buckets.EnumerateArray()
                        .Select(ReadDailyBucket)
                        .Where(item => item is not null)
                        .Select(item => item!)
                        .OrderBy(item => item.Date)
                        .TakeLast(30)
                        .ToArray()
                    : [];

            return new CodexUsageSnapshot(
                CodexUsageStatus.Ready,
                new CodexUsageSummary(
                    GetNullableInt64(summary, "lifetimeTokens"),
                    GetNullableInt64(summary, "peakDailyTokens"),
                    GetNullableInt64(summary, "longestRunningTurnSec"),
                    GetNullableInt64(summary, "currentStreakDays"),
                    GetNullableInt64(summary, "longestStreakDays")),
                daily,
                ReadRateLimits(limits.RootElement.GetProperty("result")),
                retrievedAt,
                Error: null);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        Process process,
        int id,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("The Codex App Server closed before replying.");
            }

            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number
                && responseId.GetInt32() == id)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static void ThrowIfError(JsonElement response, string prefix)
    {
        if (!response.TryGetProperty("error", out var error))
        {
            return;
        }

        var message = error.TryGetProperty("message", out var value)
            ? value.GetString()
            : null;
        throw new InvalidOperationException($"{prefix}: {message ?? "unknown App Server error"}.");
    }

    private static long? GetNullableInt64(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
        && property.ValueKind != JsonValueKind.Null
            ? property.GetInt64()
            : null;

    private static CodexDailyUsage? ReadDailyBucket(JsonElement bucket)
    {
        if (!bucket.TryGetProperty("startDate", out var dateValue)
            || !DateOnly.TryParse(dateValue.GetString(), out var date)
            || !bucket.TryGetProperty("tokens", out var tokensValue))
        {
            return null;
        }

        return new CodexDailyUsage(date, tokensValue.GetInt64());
    }

    private static List<CodexRateLimitWindow> ReadRateLimits(JsonElement result)
    {
        var windows = new List<CodexRateLimitWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var limitsById)
            && limitsById.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in limitsById.EnumerateObject())
            {
                ReadRateLimit(property.Name, property.Value, windows);
            }
        }
        else if (result.TryGetProperty("rateLimits", out var historical)
                 && historical.ValueKind == JsonValueKind.Object)
        {
            ReadRateLimit("codex", historical, windows);
        }

        return windows;
    }

    private static void ReadRateLimit(
        string limitId,
        JsonElement limit,
        ICollection<CodexRateLimitWindow> destination)
    {
        var name = limit.TryGetProperty("limitName", out var nameValue)
            ? nameValue.GetString()
            : null;
        AddWindow("primary", "primary", limitId, name, limit, destination);
        AddWindow("secondary", "secondary", limitId, name, limit, destination);
    }

    private static void AddWindow(
        string propertyName,
        string windowName,
        string limitId,
        string? limitName,
        JsonElement limit,
        ICollection<CodexRateLimitWindow> destination)
    {
        if (!limit.TryGetProperty(propertyName, out var window)
            || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("usedPercent", out var usedValue)
            || usedValue.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var duration = GetNullableInt64(window, "windowDurationMins");
        DateTimeOffset? resetsAt = window.TryGetProperty("resetsAt", out var resetValue)
            && resetValue.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(resetValue.GetInt64())
                : null;
        destination.Add(new CodexRateLimitWindow(
            limitId,
            limitName,
            windowName,
            usedValue.GetDouble(),
            duration,
            resetsAt));
    }

    private static CodexUsageSnapshot Unavailable(DateTimeOffset retrievedAt, string error) =>
        new(CodexUsageStatus.Unavailable, null, [], [], retrievedAt, error);
}
