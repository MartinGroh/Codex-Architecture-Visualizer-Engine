using System.Diagnostics;
using System.Text.Json;
using Cave.Domain;
using Cave.Infrastructure.Codex;

namespace Cave.Tests;

/// <summary>Verifies the native goal wire contract, exact identity, cache and bounded process lifecycle.</summary>
public sealed class CodexAppServerGoalProviderTests
{
    /// <summary>Every native lifecycle is preserved, including real zero usage and nullable budgets.</summary>
    [Theory]
    [InlineData("active", CodexGoalStatus.Active)]
    [InlineData("paused", CodexGoalStatus.Paused)]
    [InlineData("blocked", CodexGoalStatus.Blocked)]
    [InlineData("usageLimited", CodexGoalStatus.UsageLimited)]
    [InlineData("budgetLimited", CodexGoalStatus.BudgetLimited)]
    [InlineData("complete", CodexGoalStatus.Complete)]
    public async Task NativeGoalPreservesFieldsAndReadOnlyHandshake(string status, CodexGoalStatus expected)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, $$$"""
                {"goal":{"threadId":"task-a","objective":"A public goal","status":"{{{status}}}",
                "tokenBudget":null,"tokensUsed":0,"timeUsedSeconds":0,"createdAt":123,"updatedAt":456}}
                """);
            using var provider = new CodexAppServerGoalProvider(TimeProvider.System, command);

            var result = await provider.GetAsync("task-a", CancellationToken.None);

            Assert.Equal(CodexGoalSourceStatus.Ready, result.Status);
            Assert.Equal("task-a", result.SessionId);
            var goal = Assert.IsType<CodexGoal>(result.Goal);
            Assert.Equal("A public goal", goal.Objective);
            Assert.Equal(expected, goal.Status);
            Assert.Null(goal.TokenBudget);
            Assert.Equal(0L, goal.TokensUsed);
            Assert.Equal(0L, goal.TimeUsedSeconds);
            Assert.Equal(123L, goal.CreatedAt);
            Assert.Equal(456L, goal.UpdatedAt);
            Assert.Null(result.Error);
            Assert.Collection(await ReadMethodsAsync(root),
                method => Assert.Equal("initialize", method),
                method => Assert.Equal("initialized", method),
                method => Assert.Equal("thread/goal/get", method));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>An absent native goal is a successful empty result, never a fabricated objective.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"goal\":null}")]
    public async Task MissingGoalIsReadyWithoutObjective(string response)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, response);
            using var provider = new CodexAppServerGoalProvider(TimeProvider.System, command);
            var result = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.Equal(CodexGoalSourceStatus.Ready, result.Status);
            Assert.Null(result.Goal);
            Assert.Null(result.Error);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Foreign identities, unsupported values and malformed contracts cannot publish an objective.</summary>
    [Theory]
    [InlineData("foreign")]
    [InlineData("status")]
    [InlineData("number")]
    [InlineData("zeroBudget")]
    [InlineData("missing")]
    [InlineData("error")]
    [InlineData("eof")]
    [InlineData("oversized")]
    [InlineData("notifications")]
    public async Task FailedNativeReadReturnsUnavailableWithoutObjective(string mode)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var response = mode switch
            {
                "foreign" => "{\"goal\":{\"threadId\":\"task-b\",\"objective\":\"Foreign private goal\"}}",
                "status" => GoalJson("not-supported"),
                "number" => GoalJson("active").Replace("\"tokensUsed\":0", "\"tokensUsed\":\"bad\"", StringComparison.Ordinal),
                "zeroBudget" => GoalJson("active").Replace("\"tokenBudget\":1000", "\"tokenBudget\":0", StringComparison.Ordinal),
                "missing" => "{\"goal\":{\"threadId\":\"task-a\",\"objective\":\"Incomplete goal\"}}",
                _ => "{}",
            };
            var command = await CreateServerAsync(root, response, mode);
            using var provider = new CodexAppServerGoalProvider(TimeProvider.System, command);
            var result = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.Equal(CodexGoalSourceStatus.Unavailable, result.Status);
            Assert.Equal("task-a", result.SessionId);
            Assert.Null(result.Goal);
            Assert.NotNull(result.Error);
            Assert.InRange(result.Error.Length, 1, 512);
            Assert.DoesNotContain("Foreign private goal", result.Error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Two workspaces reuse only their exact task's cached goal; expiry reads fresh native state.</summary>
    [Fact]
    public async Task CacheUsesExactTaskAndExpiresAfterFifteenSeconds()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, GoalJson("active"), "perTask");
            var clock = new MutableClock();
            using var provider = new CodexAppServerGoalProvider(clock, command);
            var first = await provider.GetAsync("task-a", CancellationToken.None);
            var cached = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.Same(first, cached);
            Assert.Equal(3, (await ReadMethodsAsync(root)).Length);

            var foreign = await provider.GetAsync("task-b", CancellationToken.None);
            Assert.Equal(CodexGoalSourceStatus.Ready, foreign.Status);
            Assert.Equal("task-b", foreign.SessionId);
            Assert.Equal("Goal for task-b", foreign.Goal?.Objective);
            Assert.Equal(6, (await ReadMethodsAsync(root)).Length);

            var reread = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.Same(first, reread);
            Assert.Same(foreign, await provider.GetAsync("task-b", CancellationToken.None));
            Assert.Equal(CodexGoalSourceStatus.Ready, reread.Status);
            Assert.Equal("Goal for task-a", reread.Goal?.Objective);
            Assert.Equal(6, (await ReadMethodsAsync(root)).Length);
            clock.Now += TimeSpan.FromSeconds(16);
            var expired = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.NotSame(reread, expired);
            Assert.Equal(9, (await ReadMethodsAsync(root)).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A cancelled stalled lookup propagates cancellation and terminates its exact owned child.</summary>
    [Fact]
    public async Task CancellationStopsOwnedAppServer()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, "{}", "stall");
            using var provider = new CodexAppServerGoalProvider(TimeProvider.System, command);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetAsync("task-a", cancellation.Token));
            AssertOwnedServerExited(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>The adapter's own time bound returns unavailable and closes a stalled process.</summary>
    [Fact]
    public async Task TimeoutIsExplicitAndStopsOwnedAppServer()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, "{}", "stall");
            using var provider = new CodexAppServerGoalProvider(TimeProvider.System, command);
            var watch = Stopwatch.StartNew();
            var result = await provider.GetAsync("task-a", CancellationToken.None);
            Assert.Equal(CodexGoalSourceStatus.Unavailable, result.Status);
            Assert.Null(result.Goal);
            Assert.Contains("timed out", result.Error, StringComparison.Ordinal);
            Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20));
            AssertOwnedServerExited(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertOwnedServerExited(string root)
    {
        var pid = int.Parse(File.ReadAllText(Path.Combine(root, "server.pid")), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.WaitForExit(2_000), "The task-owned App Server must stop with the lookup.");
        }
        catch (ArgumentException)
        {
            // The exact child was already reaped.
        }
    }

    private static string GoalJson(string status) => $$$"""
        {"goal":{"threadId":"task-a","objective":"A public goal","status":"{{{status}}}",
        "tokenBudget":1000,"tokensUsed":0,"timeUsedSeconds":0,"createdAt":123,"updatedAt":456}}
        """;

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-goal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static Task<string[]> ReadMethodsAsync(string root) => File.ReadAllLinesAsync(Path.Combine(root, "methods.txt"));

    private static async Task<string> CreateServerAsync(string root, string response, string mode = "ready")
    {
        var command = Path.Combine(root, "fake_codex.cmd");
        var script = """
            import json, os, sys, time
            root = os.path.dirname(__file__)
            mode = __MODE__
            response = json.loads(__RESPONSE__)
            with open(os.path.join(root, "server.pid"), "w") as f: f.write(str(os.getpid()))
            initialized = False
            def send(value): print(json.dumps(value, separators=(",", ":")), flush=True)
            for line in sys.stdin:
                request = json.loads(line)
                method = request.get("method")
                with open(os.path.join(root, "methods.txt"), "a") as f: f.write(method + "\n")
                if method == "initialize":
                    assert request["params"]["capabilities"]["experimentalApi"] is True
                    assert "clientInfo" in request["params"]
                    send({"id":request["id"],"result":{"userAgent":"goal-test"}})
                elif method == "initialized": initialized = True
                elif method == "thread/goal/get":
                    assert initialized
                    assert request["params"]["threadId"] in ["task-a", "task-b"]
                    if mode == "stall": time.sleep(60)
                    elif mode == "eof": sys.exit(0)
                    elif mode == "error": send({"id":request["id"],"error":{"code":-32601,"message":"not supported"}})
                    elif mode == "oversized": print("x" * 140000, flush=True)
                    elif mode == "notifications":
                        for i in range(70): send({"method":"unrelated","params":{}})
                    elif mode == "perTask":
                        response["goal"]["threadId"] = request["params"]["threadId"]
                        response["goal"]["objective"] = "Goal for " + request["params"]["threadId"]
                        send({"id":request["id"],"result":response})
                    else: send({"id":request["id"],"result":response})
                else: raise AssertionError("Goal reads must never mutate or enumerate tasks: " + str(method))
            """
            .Replace("__MODE__", JsonSerializer.Serialize(mode), StringComparison.Ordinal)
            .Replace("__RESPONSE__", JsonSerializer.Serialize(response), StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "fake_goal_server.py"), script);
        await File.WriteAllTextAsync(command, "@echo off\r\npython \"%~dp0fake_goal_server.py\" %*\r\n");
        return command;
    }

    private sealed class MutableClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
