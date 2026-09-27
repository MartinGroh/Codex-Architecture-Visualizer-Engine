using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Codex;

/// <summary>Reads only the native goal endpoint through the canonical local App Server process.</summary>
public sealed class CodexAppServerGoalProvider : ICodexGoalProvider, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const int MaxCachedTasks = 32;
    private readonly TimeProvider _timeProvider;
    private readonly string? _codexCommand;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CodexGoalSnapshot> _cache = new(StringComparer.Ordinal);

    /// <summary>Creates a bounded, read-only provider with at most 32 exact-task cached results.</summary>
    /// <param name="timeProvider">The clock used for cache freshness and lookup timestamps.</param>
    /// <param name="codexCommand">An explicit Codex command, or null for canonical PATH resolution.</param>
    public CodexAppServerGoalProvider(TimeProvider timeProvider, string? codexCommand = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _codexCommand = codexCommand;
    }

    /// <inheritdoc />
    public async Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var now = _timeProvider.GetUtcNow();
        try
        {
            await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(sessionId, now, "The native Codex goal lookup timed out.");
        }

        try
        {
            now = _timeProvider.GetUtcNow();
            foreach (var key in _cache.Where(pair => now < pair.Value.RetrievedAtUtc
                         || now - pair.Value.RetrievedAtUtc >= CacheDuration).Select(pair => pair.Key).ToArray())
            {
                _cache.Remove(key);
            }

            if (_cache.TryGetValue(sessionId, out var cached))
            {
                return cached;
            }

            CodexGoalSnapshot result;
            try
            {
                result = await ReadAsync(sessionId, now, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = Unavailable(sessionId, now, "The native Codex goal lookup timed out.");
            }
            catch (Exception exception) when (exception is IOException or JsonException
                or InvalidOperationException or Win32Exception or ArgumentException or FormatException or KeyNotFoundException)
            {
                result = Unavailable(sessionId, now, $"The native Codex goal is unavailable: {exception.Message}");
            }

            if (_cache.Count >= MaxCachedTasks)
            {
                _cache.Remove(_cache.MinBy(pair => pair.Value.RetrievedAtUtc).Key);
            }

            _cache[sessionId] = result;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<CodexGoalSnapshot> ReadAsync(
        string sessionId,
        DateTimeOffset retrievedAt,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(_codexCommand) };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Codex App Server could not be started.");
        }

        var stderr = DrainAsync(process.StandardError, cancellationToken);
        try
        {
            var reader = new BoundedLineReader(process.StandardOutput);
            await CodexAppServerProcess.WriteAsync(process, new
            {
                method = "initialize",
                id = 1,
                @params = new
                {
                    clientInfo = new { name = "cave", version = "0.3.0" },
                    capabilities = new { experimentalApi = true },
                },
            }, cancellationToken).ConfigureAwait(false);
            using var initialize = await ReadResponseAsync(reader, 1, cancellationToken).ConfigureAwait(false);
            ThrowIfError(initialize.RootElement);
            await CodexAppServerProcess.WriteAsync(process,
                new { method = "initialized", @params = new { } }, cancellationToken).ConfigureAwait(false);
            await CodexAppServerProcess.WriteAsync(process,
                new { method = "thread/goal/get", id = 2, @params = new { threadId = sessionId } },
                cancellationToken).ConfigureAwait(false);
            using var response = await ReadResponseAsync(reader, 2, cancellationToken).ConfigureAwait(false);
            ThrowIfError(response.RootElement);
            var result = response.RootElement.GetProperty("result");
            if (result.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("The native goal response must be an object.");
            }

            var goal = result.TryGetProperty("goal", out var nativeGoal)
                && nativeGoal.ValueKind != JsonValueKind.Null
                    ? ReadGoal(nativeGoal, sessionId)
                    : null;
            return new CodexGoalSnapshot(CodexGoalSourceStatus.Ready, sessionId, goal, retrievedAt, null);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The owned process may have exited between the identity check and kill.
            }

            try
            {
                await stderr.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException)
            {
                // This discarded diagnostic drain is cancelled or closed with the owned process.
            }
        }
    }

    private static CodexGoal ReadGoal(JsonElement goal, string sessionId)
    {
        if (goal.ValueKind != JsonValueKind.Object
            || !string.Equals(goal.GetProperty("threadId").GetString(), sessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The native goal did not match the exact workspace task.");
        }

        var objective = goal.GetProperty("objective").GetString();
        if (string.IsNullOrWhiteSpace(objective))
        {
            throw new InvalidOperationException("The native goal objective is missing.");
        }

        var status = goal.GetProperty("status").GetString() switch
        {
            "active" => CodexGoalStatus.Active,
            "paused" => CodexGoalStatus.Paused,
            "blocked" => CodexGoalStatus.Blocked,
            "usageLimited" => CodexGoalStatus.UsageLimited,
            "budgetLimited" => CodexGoalStatus.BudgetLimited,
            "complete" => CodexGoalStatus.Complete,
            _ => throw new InvalidOperationException("The native goal status is unsupported."),
        };
        var budget = goal.TryGetProperty("tokenBudget", out var value) && value.ValueKind != JsonValueKind.Null
            ? (long?)value.GetInt64() : null;
        var tokens = goal.GetProperty("tokensUsed").GetInt64();
        var seconds = goal.GetProperty("timeUsedSeconds").GetInt64();
        if (budget is <= 0 || tokens < 0 || seconds < 0)
        {
            throw new InvalidOperationException("The native goal budget or usage value is invalid.");
        }

        return new CodexGoal(objective, status, budget, tokens, seconds,
            goal.GetProperty("createdAt").GetInt64(), goal.GetProperty("updatedAt").GetInt64());
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        BoundedLineReader reader,
        int id,
        CancellationToken cancellationToken)
    {
        for (var count = 0; count < 64; count++)
        {
            var line = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("The Codex App Server closed before replying.");
            }

            var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("method", out _)
                && document.RootElement.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number && responseId.TryGetInt32(out var responseNumber)
                && responseNumber == id)
            {
                return document;
            }

            document.Dispose();
        }

        throw new InvalidOperationException("The Codex App Server exceeded the bounded response count.");
    }

    private static void ThrowIfError(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var value) ? value.ToString() : "unknown";
            throw new InvalidOperationException($"The native goal endpoint returned error code {code}.");
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            // Discard stderr with fixed memory while the bounded process request is active.
        }
    }

    private static CodexGoalSnapshot Unavailable(string sessionId, DateTimeOffset retrievedAt, string error) =>
        new(CodexGoalSourceStatus.Unavailable, sessionId, null, retrievedAt,
            error.Length <= 512 ? error : error[..512]);

    private sealed class BoundedLineReader(StreamReader reader)
    {
        private readonly char[] _buffer = new char[4096];
        private int _position;
        private int _count;

        internal async Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_position == _count)
                {
                    _count = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    _position = 0;
                    if (_count == 0)
                    {
                        return line.Length == 0 ? null : line.ToString();
                    }
                }

                var character = _buffer[_position++];
                if (character == '\n')
                {
                    return line.ToString().TrimEnd('\r');
                }

                if (line.Length >= 131_072)
                {
                    throw new InvalidOperationException("The native goal response exceeded the 131072 character limit.");
                }

                line.Append(character);
            }
        }
    }
}
