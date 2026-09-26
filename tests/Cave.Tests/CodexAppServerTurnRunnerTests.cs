using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Codex;
using Cave.Infrastructure.Conversation;

namespace Cave.Tests;

/// <summary>
/// Verifies CAVE's exact-task Codex App Server protocol boundary with a real child process.
/// </summary>
public sealed class CodexAppServerTurnRunnerTests
{
    /// <summary>Verifies valid fail-closed replies to server callbacks for both shared and isolated turns.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerRequestsAreDeclinedOrAnsweredWithTheirCurrentContract(bool ephemeral)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "session-exact", serverRequests: true);
            var clock = new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_790_424_000));
            var runner = new CodexAppServerTurnRunner(new RecordingConversationStore(), clock, command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var result = ephemeral
                ? await runner.RunEphemeralAsync(root, "session-exact", "Explain the box", timeout.Token)
                : await runner.RunAsync(
                    root, "session-exact", "callback-message", "Continue", (_, _) => Task.CompletedTask, timeout.Token);

            Assert.Equal(ephemeral ? "Working from browser." : "turn-browser", result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the complete handshake, exact task validation, turn start, and public-message streaming flow.
    /// </summary>
    [Fact]
    public async Task RunAsyncStreamsPublicMessagesFromExactTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "session-exact");
            var store = new RecordingConversationStore();
            var runner = new CodexAppServerTurnRunner(store, TimeProvider.System, command);
            string? startedTurnId = null;

            var completedTurnId = await runner.RunAsync(
                root,
                "session-exact",
                "browser-message-1",
                "Continue from browser",
                (turnId, _) =>
                {
                    startedTurnId = turnId;
                    return Task.CompletedTask;
                },
                CancellationToken.None);

            Assert.Equal("turn-browser", startedTurnId);
            Assert.Equal("turn-browser", completedTurnId);
            Assert.Contains(store.Messages, message =>
                message.Role == ConversationRole.User
                && message.SessionId == "session-exact"
                && message.TurnId == "turn-browser"
                && message.Text == "Continue from browser");
            Assert.Contains(store.Messages, message =>
                message.Role == ConversationRole.Assistant
                && message.Kind == ConversationMessageKind.Commentary
                && message.IsStreaming
                && message.Text == "Working from browser.");
            Assert.Contains(store.Messages, message =>
                message.Role == ConversationRole.Assistant
                && message.Kind == ConversationMessageKind.Final
                && !message.IsStreaming
                && message.Text == "Working from browser.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that node questions use an in-memory fork and never publish into the shared transcript.
    /// </summary>
    [Fact]
    public async Task RunEphemeralAsyncForksExactTaskWithoutRequiringMainWriter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "session-exact", activeWriterOnResume: true);
            var store = new RecordingConversationStore();
            var runner = new CodexAppServerTurnRunner(store, TimeProvider.System, command);

            var answer = await runner.RunEphemeralAsync(
                root,
                "session-exact",
                "Explain the selected box",
                CancellationToken.None);

            Assert.Equal("Working from browser.", answer);
            Assert.Empty(store.Messages);
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(root, EphemeralConversationSession.MarkerDirectory.Replace('/', Path.DirectorySeparatorChar)),
                "*.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a resumed App Server task may never differ from the hook-bound task.
    /// </summary>
    [Fact]
    public async Task RunAsyncRejectsDifferentResolvedTask()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "session-other");
            var store = new RecordingConversationStore();
            var runner = new CodexAppServerTurnRunner(store, TimeProvider.System, command);

            var exception = await Assert.ThrowsAsync<CodexTaskConflictException>(() => runner.RunAsync(
                root,
                "session-exact",
                "browser-message-2",
                "Do not redirect me",
                (_, _) => Task.CompletedTask,
                CancellationToken.None));

            Assert.Contains("different task", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(store.Messages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that exclusive task ownership is surfaced as retryable before a browser turn is accepted.
    /// </summary>
    [Fact]
    public async Task RunAsyncReportsActiveWriterBeforeStartingTurn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "session-exact", activeWriterOnResume: true);
            var store = new RecordingConversationStore();
            var runner = new CodexAppServerTurnRunner(store, TimeProvider.System, command);
            string? startedTurnId = null;

            var exception = await Assert.ThrowsAsync<CodexTaskWriterBusyException>(() => runner.RunAsync(
                root,
                "session-exact",
                "browser-message-busy",
                "Keep this queued",
                (turnId, _) =>
                {
                    startedTurnId = turnId;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

            Assert.Contains("will retry", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(startedTurnId);
            Assert.Empty(store.Messages);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-app-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> CreateFakeAppServerAsync(
        string root,
        string resolvedSessionId,
        bool activeWriterOnResume = false,
        bool serverRequests = false)
    {
        var pythonPath = Path.Combine(root, "fake_app_server.py");
        var commandPath = Path.Combine(root, "fake_codex.cmd");
        var python = """
            import json
            import sys

            resolved_session_id = __RESOLVED_SESSION_ID__
            active_writer_on_resume = __ACTIVE_WRITER_ON_RESUME__
            server_requests = __SERVER_REQUESTS__
            experimental_api = False

            def send(value):
                sys.stdout.write(json.dumps(value, separators=(",", ":")) + "\n")
                sys.stdout.flush()

            for line in sys.stdin:
                request = json.loads(line)
                method = request.get("method")
                request_id = request.get("id")
                if method == "initialize":
                    experimental_api = request["params"].get("capabilities", {}).get("experimentalApi", False)
                    send({"id": request_id, "result": {"userAgent": "cave-test"}})
                elif method == "thread/resume" and active_writer_on_resume:
                    send({"id": request_id, "error": {
                        "code": -32603,
                        "message": "thread session-exact already has an active writer"
                    }})
                elif method in ("thread/read", "thread/resume"):
                    if method == "thread/resume":
                        assert experimental_api and request["params"].get("excludeTurns") is True, "Metadata-only resume requires experimental capability"
                    send({"id": request_id, "result": {"thread": {
                        "id": resolved_session_id,
                        "sessionId": resolved_session_id,
                        "status": "notLoaded"
                    }}})
                elif method == "thread/fork":
                    assert experimental_api and request["params"].get("excludeTurns") is True, "Paginated ephemeral forks require excludeTurns and experimental capability"
                    assert request["params"].get("deferGoalContinuation") is True, "A memo must defer the inherited goal's automatic continuation"
                    source_id = request["params"]["threadId"]
                    send({"id": request_id, "result": {"thread": {
                        "id": "temporary-thread",
                        "sessionId": "temporary-thread",
                        "forkedFromId": source_id,
                        "ephemeral": True
                    }}})
                elif method == "turn/start":
                    session_id = request["params"]["threadId"]
                    turn_id = "turn-browser"
                    item_id = "assistant-browser"
                    if server_requests:
                        callbacks = [
                            ("item/commandExecution/requestApproval", {"decision": "decline"}),
                            ("item/fileChange/requestApproval", {"decision": "decline"}),
                            ("item/tool/requestUserInput", {"answers": {}}),
                            ("item/permissions/requestApproval", {"permissions": {}, "scope": "turn"}),
                            ("mcpServer/elicitation/request", {"action": "decline"}),
                            ("item/tool/call", {"success": False, "contentItems": [
                                {"type": "inputText", "text": "CAVE does not execute client-provided tools."}
                            ]}),
                            ("currentTime/read", {"currentTimeAt": 1790424000}),
                            ("unsupported/server/request", None),
                        ]
                        for index, (callback, expected) in enumerate(callbacks):
                            callback_id = "server-callback-" + str(index)
                            send({"id": callback_id, "method": callback, "params": {
                                "threadId": session_id, "turnId": turn_id
                            }})
                            response = json.loads(sys.stdin.readline())
                            assert response["id"] == callback_id, "Callback request IDs must survive unchanged"
                            if expected is None:
                                assert "result" not in response and response["error"]["code"] == -32601, "Unknown requests need an unsupported-method error"
                            else:
                                assert "error" not in response and response["result"] == expected, "Invalid callback response: " + repr(response)
                    send({"id": request_id, "result": {"turn": {"id": turn_id, "status": "inProgress"}}})
                    send({"method": "item/started", "params": {
                        "threadId": session_id,
                        "turnId": turn_id,
                        "startedAtMs": 1787475600000,
                        "item": {"type": "agentMessage", "id": item_id, "text": "", "phase": "commentary"}
                    }})
                    send({"method": "item/agentMessage/delta", "params": {
                        "threadId": session_id,
                        "turnId": turn_id,
                        "itemId": item_id,
                        "delta": "Working "
                    }})
                    send({"method": "item/agentMessage/delta", "params": {
                        "threadId": session_id,
                        "turnId": turn_id,
                        "itemId": item_id,
                        "delta": "from browser."
                    }})
                    send({"method": "item/completed", "params": {
                        "threadId": session_id,
                        "turnId": turn_id,
                        "completedAtMs": 1787475601000,
                        "item": {
                            "type": "agentMessage",
                            "id": item_id,
                            "text": "Working from browser.",
                            "phase": "final_answer"
                        }
                    }})
                    send({"method": "turn/completed", "params": {
                        "threadId": session_id,
                        "turn": {"id": turn_id, "status": "completed", "error": None}
                    }})
            """.Replace(
                "__RESOLVED_SESSION_ID__",
                System.Text.Json.JsonSerializer.Serialize(resolvedSessionId),
                StringComparison.Ordinal).Replace(
                    "__ACTIVE_WRITER_ON_RESUME__",
                    activeWriterOnResume ? "True" : "False",
                    StringComparison.Ordinal).Replace(
                        "__SERVER_REQUESTS__",
                        serverRequests ? "True" : "False",
                        StringComparison.Ordinal);
        await File.WriteAllTextAsync(pythonPath, python);
        await File.WriteAllTextAsync(
            commandPath,
            "@echo off\r\npython \"%~dp0fake_app_server.py\" %*\r\n");
        return commandPath;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingConversationStore : IConversationStore
    {
        public List<ConversationMessage> Messages { get; } = [];

        public Task<ConversationOverlay> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken) =>
            Task.FromResult(ConversationOverlay.Disabled);

        public Task SetSharingAsync(
            string workspaceRoot,
            bool enabled,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertAsync(
            string workspaceRoot,
            ConversationMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
