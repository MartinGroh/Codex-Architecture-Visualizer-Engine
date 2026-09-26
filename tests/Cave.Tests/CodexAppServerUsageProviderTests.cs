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

    /// <summary>A present invalid token count must produce an unavailable response instead of becoming zero.</summary>
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

            Assert.Equal(CodexUsageStatus.Unavailable, usage.Status);
            Assert.Null(usage.Summary);
            Assert.NotNull(usage.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-usage-contract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> CreateFakeAppServerAsync(string root, string usageJson)
    {
        var pythonPath = Path.Combine(root, "fake_usage_server.py");
        var commandPath = Path.Combine(root, "fake_codex.cmd");
        var python = """
            import json
            import sys

            usage = json.loads(__USAGE_JSON__)
            initialized = False

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
                    send({"id":request["id"],"result":usage})
                elif method == "account/rateLimits/read":
                    send({"id":request["id"],"result":{
                        "rateLimits":{"primary":{"usedPercent":90}},
                        "rateLimitsByLimitId":{"codex":{
                            "limitName":"Codex",
                            "primary":{"usedPercent":0,"windowDurationMins":None,"resetsAt":None},
                            "secondary":{"usedPercent":35,"windowDurationMins":10080,"resetsAt":1790510400}
                        }}
                    }})
                else:
                    raise AssertionError("Usage adapter must not start or mutate a thread: " + str(method))
            """.Replace("__USAGE_JSON__", JsonSerializer.Serialize(usageJson), StringComparison.Ordinal);
        await File.WriteAllTextAsync(pythonPath, python);
        await File.WriteAllTextAsync(commandPath, "@echo off\r\npython \"%~dp0fake_usage_server.py\" %*\r\n");
        return commandPath;
    }
}
