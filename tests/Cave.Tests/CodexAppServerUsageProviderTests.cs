using System.Text.Json;
using Cave.Application;
using Cave.Infrastructure.Codex;

namespace Cave.Tests;

/// <summary>Verifies the current Codex usage wire contract through the process adapter.</summary>
public sealed class CodexAppServerUsageProviderTests
{
    /// <summary>Preserves optional/null summary fields, real zeros, and the authoritative multi-bucket limits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsyncPreservesNullableSummaryAndRateWindows(bool emptySummary)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var summary = emptySummary ? "{}" : """
                {"lifetimeTokens":null,"peakDailyTokens":0,"longestStreakDays":0}
                """;
            var command = await CreateFakeAppServerAsync(root, $$"""
                {"summary":{{summary}},"dailyUsageBuckets":null}
                """);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var usage = await provider.GetAsync(timeout.Token);

            Assert.Equal(CodexUsageStatus.Ready, usage.Status);
            Assert.NotNull(usage.Summary);
            Assert.Null(usage.Summary.LifetimeTokens);
            Assert.Null(usage.Summary.CurrentStreakDays);
            Assert.Null(usage.Summary.LongestRunningTurnSeconds);
            Assert.Equal(emptySummary ? (long?)null : 0L, usage.Summary.PeakDailyTokens);
            Assert.Equal(emptySummary ? (long?)null : 0L, usage.Summary.LongestStreakDays);
            Assert.Empty(usage.Daily);
            Assert.Null(usage.OrdinaryUsageAllowed);
            var primary = Assert.Single(usage.RateLimits, window => window.Window == "primary");
            Assert.Equal("codex", primary.LimitId);
            Assert.Equal(0, primary.UsedPercent);
            Assert.Null(primary.WindowDurationMinutes);
            Assert.Null(primary.ResetsAtUtc);
            var secondary = Assert.Single(usage.RateLimits, window => window.Window == "secondary");
            Assert.Equal(35, secondary.UsedPercent);
            Assert.Equal(10_080L, secondary.WindowDurationMinutes);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_790_510_400), secondary.ResetsAtUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A malformed token count remains unavailable while the independent quota read is retained.</summary>
    [Fact]
    public async Task GetAsyncRejectsMalformedSummaryValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, """
                {"summary":{"lifetimeTokens":"not-a-number"}}
                """);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var usage = await provider.GetAsync(timeout.Token);

            Assert.Equal(CodexUsageStatus.Ready, usage.Status);
            Assert.Null(usage.Summary);
            Assert.NotEmpty(usage.RateLimits);
            Assert.NotNull(usage.Error);
            Assert.Contains("token activity is unavailable", usage.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Either account request can fail without suppressing the other source or exposing account error text.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GetAsyncRetainsIndependentSuccessfulRead(bool failUsage, bool failLimits)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, """
                {"summary":{"lifetimeTokens":0},"dailyUsageBuckets":[{"startDate":"2026-09-27","tokens":0}]}
                """, failUsage: failUsage, failLimits: failLimits);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var usage = await provider.GetAsync(timeout.Token);
            Assert.Equal(failUsage && failLimits ? CodexUsageStatus.Unavailable : CodexUsageStatus.Ready, usage.Status);
            Assert.Equal(failUsage, usage.Summary is null);
            Assert.Equal(failLimits, usage.RateLimits.Count == 0);
            Assert.NotNull(usage.Error);
            Assert.DoesNotContain("private-account@example.test", usage.Error);
            if (!failUsage)
            {
                Assert.Equal(0L, usage.Summary!.LifetimeTokens);
                Assert.Equal(0L, Assert.Single(usage.Daily).Tokens);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Backend usage permission remains true, false, or unavailable independently from quota percentages.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public async Task GetAsyncPreservesBackendPermission(string permission, bool? expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "{\"summary\":{}}", limitsJson: """
                {"rateLimits":{},"ordinaryUsageAllowed":__PERMISSION__,"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":0}}}}
                """.Replace("__PERMISSION__", permission, StringComparison.Ordinal));
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            var usage = await provider.GetAsync(CancellationToken.None);
            Assert.Equal(CodexUsageStatus.Ready, usage.Status);
            Assert.Null(usage.Error);
            Assert.Equal(expected, usage.OrdinaryUsageAllowed);
            Assert.Equal(0, Assert.Single(usage.RateLimits).UsedPercent);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Malformed quota values are errors while valid zero token activity remains available.</summary>
    [Theory]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"broken\"}}}")]
    [InlineData("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"resetsAt\":\"broken\"}}}")]
    [InlineData("{\"rateLimits\":{},\"rateLimitsByLimitId\":\"broken\"}")]
    [InlineData("{\"rateLimits\":{},\"ordinaryUsageAllowed\":\"false\"}")]
    public async Task GetAsyncRejectsMalformedQuotaWithoutLosingSummary(string limitsJson)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "{\"summary\":{\"lifetimeTokens\":0}}", limitsJson: limitsJson);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            var usage = await provider.GetAsync(CancellationToken.None);
            Assert.Equal(CodexUsageStatus.Ready, usage.Status);
            Assert.Equal(0L, usage.Summary!.LifetimeTokens);
            Assert.Empty(usage.RateLimits);
            Assert.Null(usage.OrdinaryUsageAllowed);
            Assert.Contains("quota is unavailable", usage.Error);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>The historical single-bucket response preserves its supplied metered identity.</summary>
    [Fact]
    public async Task GetAsyncPreservesLegacyBucketIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "{\"summary\":{}}", limitsJson: """
                {"rateLimits":{"limitId":"code-review","primary":{"usedPercent":0}},"rateLimitsByLimitId":null}
                """);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            var usage = await provider.GetAsync(CancellationToken.None);
            Assert.Null(usage.Error);
            Assert.Equal("code-review", Assert.Single(usage.RateLimits).LimitId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A missing token response reaches the bounded deadline without discarding the received quota response.</summary>
    [Fact]
    public async Task GetAsyncRetainsQuotaWhenTokenReadTimesOut()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "{\"summary\":{}}", dropUsage: true);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var usage = await provider.GetAsync(timeout.Token);
            Assert.Equal(CodexUsageStatus.Ready, usage.Status);
            Assert.Null(usage.Summary);
            Assert.NotEmpty(usage.RateLimits);
            Assert.Contains("timed out", usage.Error);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Explicit caller cancellation is propagated instead of being reported as partial success.</summary>
    [Fact]
    public async Task GetAsyncPropagatesCallerCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateFakeAppServerAsync(root, "{\"summary\":{}}", dropUsage: true);
            using var provider = new CodexAppServerUsageProvider(TimeProvider.System, command);
            using var cancellation = new CancellationTokenSource();
            var read = provider.GetAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-usage-contract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> CreateFakeAppServerAsync(
        string root, string usageJson, string? limitsJson = null,
        bool failUsage = false, bool failLimits = false, bool dropUsage = false)
    {
        var pythonPath = Path.Combine(root, "fake_usage_server.py");
        var commandPath = Path.Combine(root, "fake_codex.cmd");
        var python = """
            import json
            import sys

            usage = json.loads(__USAGE_JSON__)
            limits = json.loads(__LIMITS_JSON__)
            fail_usage = __FAIL_USAGE__
            fail_limits = __FAIL_LIMITS__
            drop_usage = __DROP_USAGE__
            initialized = False
            usage_request = None

            def send(value):
                print(json.dumps(value, separators=(",", ":")), flush=True)

            for line in sys.stdin:
                request = json.loads(line)
                method = request.get("method")
                if method == "initialize":
                    assert "clientInfo" in request["params"]
                    send({"id":request["id"],"result":{"userAgent":"cave-contract-test"}})
                elif method == "initialized":
                    initialized = True
                elif method == "account/usage/read":
                    assert initialized, "Usage must wait for the initialization handshake"
                    usage_request = request
                elif method == "account/rateLimits/read":
                    assert initialized
                    assert request["params"] == {"excludeResetCreditDetails":True}
                    assert usage_request is not None, "Both reads must be sent before waiting for a reply"
                    # Reply in reverse order to exercise the one-reader correlation path.
                    error = {"code":-32000,"message":"private-account@example.test"}
                    send({"id":request["id"], **({"error":error} if fail_limits else {"result":limits})})
                    if not drop_usage:
                        send({"id":usage_request["id"], **({"error":error} if fail_usage else {"result":usage})})
                else:
                    raise AssertionError("Usage adapter must not start or mutate a thread: " + str(method))
            """.Replace("__USAGE_JSON__", JsonSerializer.Serialize(usageJson), StringComparison.Ordinal)
                .Replace("__LIMITS_JSON__", JsonSerializer.Serialize(limitsJson ?? """
                    {"rateLimits":{"primary":{"usedPercent":90}},"rateLimitsByLimitId":{"codex":{
                        "limitName":"Codex","primary":{"usedPercent":0,"windowDurationMins":null,"resetsAt":null},
                        "secondary":{"usedPercent":35,"windowDurationMins":10080,"resetsAt":1790510400}}}}
                    """), StringComparison.Ordinal)
                .Replace("__FAIL_USAGE__", failUsage ? "True" : "False", StringComparison.Ordinal)
                .Replace("__FAIL_LIMITS__", failLimits ? "True" : "False", StringComparison.Ordinal)
                .Replace("__DROP_USAGE__", dropUsage ? "True" : "False", StringComparison.Ordinal);
        await File.WriteAllTextAsync(pythonPath, python);
        await File.WriteAllTextAsync(commandPath, "@echo off\r\npython \"%~dp0fake_usage_server.py\" %*\r\n");
        return commandPath;
    }
}
