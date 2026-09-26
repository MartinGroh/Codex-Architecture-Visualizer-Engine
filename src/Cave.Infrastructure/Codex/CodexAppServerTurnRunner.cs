using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Conversation;

namespace Cave.Infrastructure.Codex;

/// <summary>
/// Resumes an exact persisted Codex task and streams one browser-originated turn through App Server.
/// </summary>
public sealed class CodexAppServerTurnRunner : ICodexTurnRunner
{
    private readonly IConversationStore _conversationStore;
    private readonly TimeProvider _timeProvider;
    private readonly string? _codexCommand;

    /// <summary>Initializes the App Server turn runner.</summary>
    public CodexAppServerTurnRunner(
        IConversationStore conversationStore,
        TimeProvider timeProvider,
        string? codexCommand = null)
    {
        _conversationStore = conversationStore;
        _timeProvider = timeProvider;
        _codexCommand = string.IsNullOrWhiteSpace(codexCommand) ? null : codexCommand.Trim();
    }

    /// <inheritdoc />
    public async Task<string> RunAsync(
        string workspaceRoot,
        string sessionId,
        string clientMessageId,
        string text,
        Func<string, CancellationToken, Task> turnStarted,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(turnStarted);

        using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(_codexCommand) };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Codex App Server process could not be started.");
        }

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var streams = new Dictionary<string, StreamedAgentMessage>(StringComparer.Ordinal);
        try
        {
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "initialize",
                    id = 1,
                    @params = new
                    {
                        clientInfo = new { name = "cave-browser-bridge", version = "0.3.0" },
                        capabilities = new { experimentalApi = true },
                    },
                },
                cancellationToken).ConfigureAwait(false);
            using (var initialize = await ReadResponseAsync(
                       process,
                       requestId: 1,
                       workspaceRoot,
                       sessionId,
                       streams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(initialize.RootElement, "Codex App Server initialization failed");
            }

            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "initialized", @params = new { } },
                cancellationToken).ConfigureAwait(false);
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "thread/read",
                    id = 2,
                    @params = new { threadId = sessionId, includeTurns = false },
                },
                cancellationToken).ConfigureAwait(false);
            using (var read = await ReadResponseAsync(
                       process,
                       requestId: 2,
                       workspaceRoot,
                       sessionId,
                       streams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(read.RootElement, "Codex task lookup failed");
                ValidateExactTask(read.RootElement, sessionId);
            }

            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "thread/resume", id = 3, @params = new { threadId = sessionId, excludeTurns = true } },
                cancellationToken).ConfigureAwait(false);
            using (var resume = await ReadResponseAsync(
                       process,
                       requestId: 3,
                       workspaceRoot,
                       sessionId,
                       streams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfResumeError(resume.RootElement);
                ValidateExactTask(resume.RootElement, sessionId);
            }

            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "turn/start",
                    id = 4,
                    @params = new
                    {
                        threadId = sessionId,
                        clientUserMessageId = clientMessageId,
                        input = new[] { new { type = "text", text } },
                    },
                },
                cancellationToken).ConfigureAwait(false);
            string turnId;
            using (var start = await ReadResponseAsync(
                       process,
                       requestId: 4,
                       workspaceRoot,
                       sessionId,
                       streams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(start.RootElement, "Codex turn start failed");
                turnId = start.RootElement
                    .GetProperty("result")
                    .GetProperty("turn")
                    .GetProperty("id")
                    .GetString()
                    ?? throw new InvalidOperationException("Codex did not return a turn identifier.");
            }

            await turnStarted(turnId, cancellationToken).ConfigureAwait(false);
            await _conversationStore.UpsertAsync(
                workspaceRoot,
                new ConversationMessage(
                    $"appserver:user:{clientMessageId}",
                    sessionId,
                    turnId,
                    AgentId: null,
                    AgentType: "Browser",
                    IsSubagent: false,
                    ConversationRole.User,
                    ConversationMessageKind.Prompt,
                    text,
                    IsTruncated: false,
                    IsStreaming: false,
                    _timeProvider.GetUtcNow()),
                cancellationToken).ConfigureAwait(false);

            return await ReadUntilTurnCompletedAsync(
                process,
                workspaceRoot,
                sessionId,
                turnId,
                streams,
                cancellationToken).ConfigureAwait(false);
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

            try
            {
                var error = await errorTask.ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"Codex App Server stderr: {error.Trim()}");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> RunEphemeralAsync(
        string workspaceRoot,
        string sessionId,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var process = new Process { StartInfo = CodexAppServerProcess.CreateStartInfo(_codexCommand) };
        if (!process.Start())
        {
            throw new InvalidOperationException("The Codex App Server process could not be started.");
        }

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var ignoredStreams = new Dictionary<string, StreamedAgentMessage>(StringComparer.Ordinal);
        try
        {
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "initialize",
                    id = 1,
                    @params = new
                    {
                        clientInfo = new { name = "cave-node-memo", version = "0.3.0" },
                        capabilities = new { experimentalApi = true },
                    },
                },
                cancellationToken).ConfigureAwait(false);
            using (var initialize = await ReadResponseAsync(
                       process,
                       requestId: 1,
                       workspaceRoot,
                       sessionId,
                       ignoredStreams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(initialize.RootElement, "Codex App Server initialization failed");
            }

            await CodexAppServerProcess.WriteAsync(
                process,
                new { method = "initialized", @params = new { } },
                cancellationToken).ConfigureAwait(false);
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "thread/read",
                    id = 2,
                    @params = new { threadId = sessionId, includeTurns = false },
                },
                cancellationToken).ConfigureAwait(false);
            using (var read = await ReadResponseAsync(
                       process,
                       requestId: 2,
                       workspaceRoot,
                       sessionId,
                       ignoredStreams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(read.RootElement, "Codex task lookup failed");
                ValidateExactTask(read.RootElement, sessionId);
            }

            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "thread/fork",
                    id = 3,
                    // CONSTRAINT: paginated Codex threads require excludeTurns for an ephemeral fork.
                    // Deferring inherited goal work lets this explicit memo turn run before any continuation.
                    @params = new
                    {
                        threadId = sessionId,
                        ephemeral = true,
                        excludeTurns = true,
                        deferGoalContinuation = true,
                    },
                },
                cancellationToken).ConfigureAwait(false);
            string ephemeralThreadId;
            using (var fork = await ReadResponseAsync(
                       process,
                       requestId: 3,
                       workspaceRoot,
                       sessionId,
                       ignoredStreams,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(fork.RootElement, "Codex temporary side chat creation failed");
                ephemeralThreadId = ValidateEphemeralFork(fork.RootElement, sessionId);
            }

            await EphemeralConversationSession.MarkAsync(
                workspaceRoot,
                ephemeralThreadId,
                sessionId,
                _timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);

            var streams = new Dictionary<string, StreamedAgentMessage>(StringComparer.Ordinal);
            var answer = new MemoAnswer();
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    method = "turn/start",
                    id = 4,
                    @params = new
                    {
                        threadId = ephemeralThreadId,
                        input = new[] { new { type = "text", text } },
                    },
                },
                cancellationToken).ConfigureAwait(false);
            string turnId;
            using (var start = await ReadMemoResponseAsync(
                       process,
                       requestId: 4,
                       ephemeralThreadId,
                       streams,
                       answer,
                       cancellationToken).ConfigureAwait(false))
            {
                ThrowIfError(start.RootElement, "Codex temporary side chat turn start failed");
                turnId = start.RootElement
                    .GetProperty("result")
                    .GetProperty("turn")
                    .GetProperty("id")
                    .GetString()
                    ?? throw new InvalidOperationException(
                        "Codex did not return a temporary side chat turn identifier.");
            }

            return await ReadUntilMemoCompletedAsync(
                process,
                ephemeralThreadId,
                turnId,
                streams,
                answer,
                cancellationToken).ConfigureAwait(false);
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

            try
            {
                var error = await errorTask.ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"Codex App Server stderr: {error.Trim()}");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task<JsonDocument> ReadMemoResponseAsync(
        Process process,
        int requestId,
        string threadId,
        Dictionary<string, StreamedAgentMessage> streams,
        MemoAnswer answer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var document = await ReadMessageAsync(process, cancellationToken).ConfigureAwait(false);
            if (HasResponseId(document.RootElement, requestId))
            {
                return document;
            }

            await HandleMemoMessageAsync(
                process,
                document,
                threadId,
                streams,
                answer,
                cancellationToken).ConfigureAwait(false);
            document.Dispose();
        }
    }

    private async Task<string> ReadUntilMemoCompletedAsync(
        Process process,
        string threadId,
        string turnId,
        Dictionary<string, StreamedAgentMessage> streams,
        MemoAnswer answer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var document = await ReadMessageAsync(process, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var methodValue)
                && methodValue.GetString() == "turn/completed"
                && root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("threadId", out var threadValue)
                && threadValue.GetString() == threadId
                && parameters.TryGetProperty("turn", out var turn)
                && turn.TryGetProperty("id", out var turnValue)
                && turnValue.GetString() == turnId)
            {
                var status = turn.TryGetProperty("status", out var statusValue)
                    ? statusValue.GetString()
                    : null;
                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    var error = turn.TryGetProperty("error", out var errorValue)
                        && errorValue.ValueKind != JsonValueKind.Null
                            ? errorValue.ToString()
                            : null;
                    throw new InvalidOperationException(
                        $"Codex temporary side chat {turnId} ended with status '{status ?? "unknown"}'{(string.IsNullOrWhiteSpace(error) ? "." : $": {error}")}");
                }

                return answer.ReadFinal();
            }

            await HandleMemoMessageAsync(
                process,
                document,
                threadId,
                streams,
                answer,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMemoMessageAsync(
        Process process,
        JsonDocument document,
        string threadId,
        Dictionary<string, StreamedAgentMessage> streams,
        MemoAnswer answer,
        CancellationToken cancellationToken)
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("method", out var methodValue))
        {
            return;
        }

        var method = methodValue.GetString();
        if (root.TryGetProperty("id", out var requestId))
        {
            await RespondToServerRequestAsync(process, requestId, method, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!root.TryGetProperty("params", out var parameters)
            || parameters.TryGetProperty("threadId", out var threadValue)
                && threadValue.GetString() != threadId)
        {
            return;
        }

        switch (method)
        {
            case "item/started":
                CaptureStartedMessage(parameters, streams);
                break;
            case "item/agentMessage/delta":
                CaptureMemoDelta(parameters, streams, answer);
                break;
            case "item/completed":
                CaptureCompletedMemo(parameters, streams, answer);
                break;
        }
    }

    private void CaptureMemoDelta(
        JsonElement parameters,
        IDictionary<string, StreamedAgentMessage> streams,
        MemoAnswer answer)
    {
        var itemId = parameters.GetProperty("itemId").GetString()
            ?? throw new InvalidOperationException("Codex emitted an agent delta without an item id.");
        if (!streams.TryGetValue(itemId, out var stream))
        {
            stream = new StreamedAgentMessage(
                itemId,
                parameters.GetProperty("turnId").GetString(),
                phase: null,
                _timeProvider.GetUtcNow(),
                new StringBuilder());
            streams[itemId] = stream;
        }

        stream.Text.Append(parameters.GetProperty("delta").GetString());
        answer.Record(stream.Text.ToString(), stream.Phase, isStreaming: true);
    }

    private void CaptureCompletedMemo(
        JsonElement parameters,
        IDictionary<string, StreamedAgentMessage> streams,
        MemoAnswer answer)
    {
        if (!parameters.TryGetProperty("item", out var item)
            || !IsAgentMessage(item)
            || !item.TryGetProperty("id", out var idValue)
            || idValue.GetString() is not { Length: > 0 } itemId)
        {
            return;
        }

        if (!streams.TryGetValue(itemId, out var stream))
        {
            stream = new StreamedAgentMessage(
                itemId,
                parameters.GetProperty("turnId").GetString(),
                ReadPhase(item),
                _timeProvider.GetUtcNow(),
                new StringBuilder());
            streams[itemId] = stream;
        }

        stream.Phase = ReadPhase(item) ?? stream.Phase;
        var text = item.TryGetProperty("text", out var textValue)
            ? textValue.GetString()
            : stream.Text.ToString();
        answer.Record(text, stream.Phase, isStreaming: false);
    }

    private async Task<JsonDocument> ReadResponseAsync(
        Process process,
        int requestId,
        string workspaceRoot,
        string sessionId,
        Dictionary<string, StreamedAgentMessage> streams,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var document = await ReadMessageAsync(process, cancellationToken).ConfigureAwait(false);
            if (HasResponseId(document.RootElement, requestId))
            {
                return document;
            }

            await HandleMessageAsync(
                process,
                document,
                workspaceRoot,
                sessionId,
                streams,
                cancellationToken).ConfigureAwait(false);
            document.Dispose();
        }
    }

    private async Task<string> ReadUntilTurnCompletedAsync(
        Process process,
        string workspaceRoot,
        string sessionId,
        string turnId,
        Dictionary<string, StreamedAgentMessage> streams,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var document = await ReadMessageAsync(process, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("method", out var methodValue)
                && methodValue.GetString() == "turn/completed"
                && root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("threadId", out var threadValue)
                && threadValue.GetString() == sessionId
                && parameters.TryGetProperty("turn", out var turn)
                && turn.TryGetProperty("id", out var turnValue)
                && turnValue.GetString() == turnId)
            {
                var status = turn.TryGetProperty("status", out var statusValue)
                    ? statusValue.GetString()
                    : null;
                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    var error = turn.TryGetProperty("error", out var errorValue)
                        && errorValue.ValueKind != JsonValueKind.Null
                            ? errorValue.ToString()
                            : null;
                    throw new InvalidOperationException(
                        $"Codex turn {turnId} ended with status '{status ?? "unknown"}'{(string.IsNullOrWhiteSpace(error) ? "." : $": {error}")}");
                }

                return turnId;
            }

            await HandleMessageAsync(
                process,
                document,
                workspaceRoot,
                sessionId,
                streams,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(
        Process process,
        JsonDocument document,
        string workspaceRoot,
        string sessionId,
        Dictionary<string, StreamedAgentMessage> streams,
        CancellationToken cancellationToken)
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("method", out var methodValue))
        {
            return;
        }

        var method = methodValue.GetString();
        if (root.TryGetProperty("id", out var requestId))
        {
            await RespondToServerRequestAsync(process, requestId, method, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!root.TryGetProperty("params", out var parameters)
            || parameters.TryGetProperty("threadId", out var threadValue)
                && threadValue.GetString() != sessionId)
        {
            return;
        }

        switch (method)
        {
            case "item/started":
                CaptureStartedMessage(parameters, streams);
                break;
            case "item/agentMessage/delta":
                await ApplyDeltaAsync(workspaceRoot, sessionId, parameters, streams, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "item/completed":
                await CompleteItemAsync(workspaceRoot, sessionId, parameters, streams, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private void CaptureStartedMessage(
        JsonElement parameters,
        IDictionary<string, StreamedAgentMessage> streams)
    {
        if (!parameters.TryGetProperty("item", out var item)
            || !IsAgentMessage(item)
            || !item.TryGetProperty("id", out var idValue)
            || idValue.GetString() is not { Length: > 0 } itemId)
        {
            return;
        }

        var occurredAt = parameters.TryGetProperty("startedAtMs", out var timestamp)
            && timestamp.TryGetInt64(out var milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                : _timeProvider.GetUtcNow();
        streams[itemId] = new StreamedAgentMessage(
            itemId,
            parameters.GetProperty("turnId").GetString(),
            ReadPhase(item),
            occurredAt,
            new StringBuilder());
    }

    private async Task ApplyDeltaAsync(
        string workspaceRoot,
        string sessionId,
        JsonElement parameters,
        IDictionary<string, StreamedAgentMessage> streams,
        CancellationToken cancellationToken)
    {
        var itemId = parameters.GetProperty("itemId").GetString()
            ?? throw new InvalidOperationException("Codex emitted an agent delta without an item id.");
        if (!streams.TryGetValue(itemId, out var stream))
        {
            stream = new StreamedAgentMessage(
                itemId,
                parameters.GetProperty("turnId").GetString(),
                phase: null,
                _timeProvider.GetUtcNow(),
                new StringBuilder());
            streams[itemId] = stream;
        }

        stream.Text.Append(parameters.GetProperty("delta").GetString());
        if (stream.Text.Length == 0)
        {
            return;
        }

        await UpsertAgentMessageAsync(
            workspaceRoot,
            sessionId,
            stream,
            stream.Text.ToString(),
            isStreaming: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteItemAsync(
        string workspaceRoot,
        string sessionId,
        JsonElement parameters,
        IDictionary<string, StreamedAgentMessage> streams,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetProperty("item", out var item)
            || !IsAgentMessage(item)
            || !item.TryGetProperty("id", out var idValue)
            || idValue.GetString() is not { Length: > 0 } itemId)
        {
            return;
        }

        if (!streams.TryGetValue(itemId, out var stream))
        {
            var occurredAt = parameters.TryGetProperty("completedAtMs", out var timestamp)
                && timestamp.TryGetInt64(out var milliseconds)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                    : _timeProvider.GetUtcNow();
            stream = new StreamedAgentMessage(
                itemId,
                parameters.GetProperty("turnId").GetString(),
                ReadPhase(item),
                occurredAt,
                new StringBuilder());
            streams[itemId] = stream;
        }

        stream.Phase = ReadPhase(item) ?? stream.Phase;
        var text = item.TryGetProperty("text", out var textValue)
            ? textValue.GetString()
            : stream.Text.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await UpsertAgentMessageAsync(
            workspaceRoot,
            sessionId,
            stream,
            text,
            isStreaming: false,
            cancellationToken).ConfigureAwait(false);
    }

    private Task UpsertAgentMessageAsync(
        string workspaceRoot,
        string sessionId,
        StreamedAgentMessage stream,
        string text,
        bool isStreaming,
        CancellationToken cancellationToken) =>
        _conversationStore.UpsertAsync(
            workspaceRoot,
            new ConversationMessage(
                $"appserver:assistant:{stream.ItemId}",
                sessionId,
                stream.TurnId,
                AgentId: null,
                AgentType: "Codex",
                IsSubagent: false,
                ConversationRole.Assistant,
                string.Equals(stream.Phase, "final_answer", StringComparison.OrdinalIgnoreCase)
                    ? ConversationMessageKind.Final
                    : ConversationMessageKind.Commentary,
                text,
                IsTruncated: false,
                IsStreaming: isStreaming,
                stream.OccurredAtUtc),
            cancellationToken);

    private async Task RespondToServerRequestAsync(
        Process process,
        JsonElement requestId,
        string? method,
        CancellationToken cancellationToken)
    {
        object? result = method switch
        {
            "item/commandExecution/requestApproval" => new { decision = "decline" },
            "item/fileChange/requestApproval" => new { decision = "decline" },
            "item/tool/requestUserInput" => new { answers = new Dictionary<string, string>() },
            "item/permissions/requestApproval" => new { permissions = new { }, scope = "turn" },
            "mcpServer/elicitation/request" => new { action = "decline" },
            "item/tool/call" => new
            {
                success = false,
                contentItems = new[]
                {
                    new { type = "inputText", text = "CAVE does not execute client-provided tools." },
                },
            },
            "currentTime/read" => new { currentTimeAt = _timeProvider.GetUtcNow().ToUnixTimeSeconds() },
            _ => null,
        };
        if (result is null)
        {
            await CodexAppServerProcess.WriteAsync(
                process,
                new
                {
                    id = requestId.Clone(),
                    error = new { code = -32601, message = $"CAVE does not support the server request '{method}'." },
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }
        await CodexAppServerProcess.WriteAsync(
            process,
            new { id = requestId.Clone(), result },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadMessageAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            throw new InvalidOperationException("The Codex App Server closed before completing the turn.");
        }

        return JsonDocument.Parse(line);
    }

    private static bool HasResponseId(JsonElement root, int requestId) =>
        root.TryGetProperty("id", out var responseId)
        && responseId.ValueKind == JsonValueKind.Number
        && responseId.TryGetInt32(out var value)
        && value == requestId
        && !root.TryGetProperty("method", out _);

    private static bool IsAgentMessage(JsonElement item) =>
        item.TryGetProperty("type", out var typeValue)
        && typeValue.GetString() == "agentMessage";

    private static string? ReadPhase(JsonElement item) =>
        item.TryGetProperty("phase", out var phaseValue) && phaseValue.ValueKind == JsonValueKind.String
            ? phaseValue.GetString()
            : null;

    private static void ValidateExactTask(JsonElement response, string expectedSessionId)
    {
        var result = response.GetProperty("result");
        var thread = result.TryGetProperty("thread", out var nestedThread) ? nestedThread : result;
        var threadId = thread.GetProperty("id").GetString();
        var sessionId = thread.GetProperty("sessionId").GetString();
        if (!string.Equals(threadId, expectedSessionId, StringComparison.Ordinal)
            || !string.Equals(sessionId, expectedSessionId, StringComparison.Ordinal))
        {
            throw new CodexTaskConflictException(
                "Codex App Server resolved a different task than the hook-bound session.");
        }
    }

    private static string ValidateEphemeralFork(JsonElement response, string expectedSessionId)
    {
        var thread = response.GetProperty("result").GetProperty("thread");
        var threadId = thread.GetProperty("id").GetString();
        var forkedFromId = thread.TryGetProperty("forkedFromId", out var forkedFromValue)
            ? forkedFromValue.GetString()
            : null;
        var ephemeral = thread.TryGetProperty("ephemeral", out var ephemeralValue)
            && ephemeralValue.ValueKind == JsonValueKind.True;
        if (string.IsNullOrWhiteSpace(threadId)
            || string.Equals(threadId, expectedSessionId, StringComparison.Ordinal)
            || !string.Equals(forkedFromId, expectedSessionId, StringComparison.Ordinal)
            || !ephemeral)
        {
            throw new CodexTaskConflictException(
                "Codex App Server did not create an ephemeral fork of the hook-bound task.");
        }

        return threadId;
    }

    private static void ThrowIfError(JsonElement response, string prefix)
    {
        if (!response.TryGetProperty("error", out var error))
        {
            return;
        }

        var message = error.TryGetProperty("message", out var value) ? value.GetString() : null;
        throw new InvalidOperationException($"{prefix}: {message ?? "unknown App Server error"}.");
    }

    private static void ThrowIfResumeError(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error))
        {
            return;
        }

        var message = error.TryGetProperty("message", out var value) ? value.GetString() : null;
        if (message?.Contains("already has an active writer", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new CodexTaskWriterBusyException(
                "Codex Desktop still owns this task. CAVE will retry when the task writer is released.");
        }

        throw new InvalidOperationException(
            $"Codex task resume failed: {message ?? "unknown App Server error"}.");
    }

    private sealed class StreamedAgentMessage(
        string itemId,
        string? turnId,
        string? phase,
        DateTimeOffset occurredAtUtc,
        StringBuilder text)
    {
        public string ItemId { get; } = itemId;
        public string? TurnId { get; } = turnId;
        public string? Phase { get; set; } = phase;
        public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;
        public StringBuilder Text { get; } = text;
    }

    private sealed class MemoAnswer
    {
        private string? _latestText;
        private string? _finalText;

        public void Record(string? text, string? phase, bool isStreaming)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _latestText = text.Trim();
            if (!isStreaming && string.Equals(phase, "final_answer", StringComparison.OrdinalIgnoreCase))
            {
                _finalText = _latestText;
            }
        }

        public string ReadFinal() =>
            _finalText
            ?? _latestText
            ?? throw new InvalidOperationException(
                "Codex completed the temporary side chat without returning an assistant answer.");
    }
}
