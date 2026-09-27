using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
                _cached = await ReadFromAppServerAsync(now, timeout.Token, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _cached = Unavailable(now, "Codex account usage timed out.");
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException
                or Win32Exception or ArgumentException or FormatException or KeyNotFoundException)
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
        CancellationToken cancellationToken,
        CancellationToken callerCancellationToken)
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
            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "account/rateLimits/read", id = 3, @params = new { excludeResetCreditDetails = true } },
                cancellationToken).ConfigureAwait(false);

            // CONSTRAINT: Token activity and quota are independent account reads. Send both
            // before waiting, use one stdout reader, and retain either successful response
            // when the other fails or the shared request deadline expires.
            var responses = new Dictionary<int, JsonDocument>();
            string? transportError = null;
            try
            {
                while (responses.Count < 2)
                {
                    try
                    {
                        var pending = responses.ContainsKey(2) ? 3 : 2;
                        var alternative = responses.Count == 0 ? 3 : (int?)null;
                        var response = await ReadResponseAsync(process, pending, cancellationToken, alternative)
                            .ConfigureAwait(false);
                        responses.Add(response.RootElement.GetProperty("id").GetInt32(), response);
                    }
                    catch (OperationCanceledException) when (!callerCancellationToken.IsCancellationRequested)
                    {
                        transportError = "The account read timed out.";
                        break;
                    }
                    catch (Exception exception) when (exception is IOException or JsonException
                        or InvalidOperationException or FormatException or KeyNotFoundException)
                    {
                        transportError = exception.Message;
                        break;
                    }
                }

                CodexUsageSummary? summary = null;
                IReadOnlyList<CodexDailyUsage> daily = [];
                IReadOnlyList<CodexRateLimitWindow> windows = [];
                bool? ordinaryUsageAllowed = null;
                var errors = new List<string>();
                var successfulReads = 0;
                if (responses.TryGetValue(2, out var usage))
                {
                    try
                    {
                        ThrowIfError(usage.RootElement, "Codex token activity request failed");
                        var result = usage.RootElement.GetProperty("result");
                        var value = result.GetProperty("summary");
                        var parsedSummary = new CodexUsageSummary(
                            GetNullableInt64(value, "lifetimeTokens"),
                            GetNullableInt64(value, "peakDailyTokens"),
                            GetNullableInt64(value, "longestRunningTurnSec"),
                            GetNullableInt64(value, "currentStreakDays"),
                            GetNullableInt64(value, "longestStreakDays"));
                        var parsedDaily = result.TryGetProperty("dailyUsageBuckets", out var buckets)
                            && buckets.ValueKind != JsonValueKind.Null
                                ? buckets.EnumerateArray().Select(ReadDailyBucket).OrderBy(item => item.Date).TakeLast(30).ToArray()
                                : [];
                        summary = parsedSummary;
                        daily = parsedDaily;
                        successfulReads++;
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException
                        or ArgumentException or FormatException or KeyNotFoundException)
                    {
                        errors.Add($"Codex token activity is unavailable: {exception.Message}");
                    }
                }
                else
                {
                    errors.Add($"Codex token activity is unavailable: {transportError}");
                }

                if (responses.TryGetValue(3, out var limits))
                {
                    try
                    {
                        ThrowIfError(limits.RootElement, "Codex rate-limit request failed");
                        var result = limits.RootElement.GetProperty("result");
                        var parsedWindows = ReadRateLimits(result);
                        var permission = result.TryGetProperty("ordinaryUsageAllowed", out var allowed)
                            && allowed.ValueKind != JsonValueKind.Null ? allowed.GetBoolean() : (bool?)null;
                        windows = parsedWindows;
                        ordinaryUsageAllowed = permission;
                        successfulReads++;
                    }
                    catch (Exception exception) when (exception is JsonException or InvalidOperationException
                        or ArgumentException or FormatException or KeyNotFoundException)
                    {
                        errors.Add($"Codex quota is unavailable: {exception.Message}");
                    }
                }
                else
                {
                    errors.Add($"Codex quota is unavailable: {transportError}");
                }

                callerCancellationToken.ThrowIfCancellationRequested();
                return new CodexUsageSnapshot(
                    successfulReads > 0 ? CodexUsageStatus.Ready : CodexUsageStatus.Unavailable,
                    summary, daily, windows, retrievedAt,
                    errors.Count == 0 ? null : string.Join(" ", errors), ordinaryUsageAllowed);
            }
            finally
            {
                foreach (var response in responses.Values)
                {
                    response.Dispose();
                }
            }
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
        CancellationToken cancellationToken,
        int? alternativeId = null)
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
                && (responseId.GetInt32() == id || responseId.GetInt32() == alternativeId))
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

        var code = error.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32().ToString(CultureInfo.InvariantCulture) : "unknown";
        // Account error text can contain account identifiers or upstream response bodies.
        throw new InvalidOperationException($"{prefix} (App Server error {code}).");
    }

    private static long? GetNullableInt64(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property)
        && property.ValueKind != JsonValueKind.Null
            ? property.GetInt64()
            : null;

    private static CodexDailyUsage ReadDailyBucket(JsonElement bucket)
    {
        if (!bucket.TryGetProperty("startDate", out var dateValue)
            || !DateOnly.TryParseExact(dateValue.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            || !bucket.TryGetProperty("tokens", out var tokensValue))
        {
            throw new FormatException("The daily usage bucket is malformed.");
        }

        return new CodexDailyUsage(date, tokensValue.GetInt64());
    }

    private static List<CodexRateLimitWindow> ReadRateLimits(JsonElement result)
    {
        var windows = new List<CodexRateLimitWindow>();
        if (result.TryGetProperty("rateLimitsByLimitId", out var limitsById)
            && limitsById.ValueKind != JsonValueKind.Null)
        {
            foreach (var property in limitsById.EnumerateObject())
            {
                ReadRateLimit(property.Name, property.Value, windows);
            }
        }
        else
        {
            var historical = result.GetProperty("rateLimits");
            var limitId = historical.TryGetProperty("limitId", out var value)
                && value.ValueKind != JsonValueKind.Null ? value.GetString()! : "codex";
            ReadRateLimit(limitId, historical, windows);
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
            || window.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var duration = GetNullableInt64(window, "windowDurationMins");
        var reset = GetNullableInt64(window, "resetsAt");
        DateTimeOffset? resetsAt = reset.HasValue ? DateTimeOffset.FromUnixTimeSeconds(reset.Value) : null;
        destination.Add(new CodexRateLimitWindow(
            limitId,
            limitName,
            windowName,
            window.GetProperty("usedPercent").GetInt32(),
            duration,
            resetsAt));
    }

    private static CodexUsageSnapshot Unavailable(DateTimeOffset retrievedAt, string error) =>
        new(CodexUsageStatus.Unavailable, null, [], [], retrievedAt, error);
}
