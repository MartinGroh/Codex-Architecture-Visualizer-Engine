using System.Text.Json;
using Cave.Infrastructure.Codex;

namespace Cave.Tests;

/// <summary>Exercises exact identity, nullable counters, caching and item-free read-only native contracts.</summary>
public sealed class CodexAppServerTelemetrySourceTests
{
    private static readonly string[] ExpectedMethods = ["initialize", "initialized", "thread/read", "account/usage/read", "thread/turns/list"];

    /// <summary>A cleanup failure retains collected values and cached safe diagnostics without exposing native text.</summary>
    [Theory]
    [InlineData("ready", true)]
    [InlineData("foreignMetadata", false)]
    [InlineData("oversized", false)]
    public async Task CleanupTimeoutRemainsVisibleWithCollectedOrMissingData(string mode, bool configuration)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var stops = 0;
            var command = await CreateServerAsync(root, mode);
            using var source = new CodexAppServerTelemetrySource(TimeProvider.System, command, async process =>
            {
                await CodexAppServerProcess.StopAsync(process);
                stops++;
                throw new TimeoutException("private-account@example.test must not appear in telemetry");
            });

            var sample = Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None));

            Assert.Equal(configuration, sample.Configuration is not null);
            Assert.Contains("cleanup failed", sample.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("private", sample.Error, StringComparison.OrdinalIgnoreCase);
            if (configuration)
            {
                Assert.Equal("gpt-6.1-sol", sample.Configuration!.Model);
                Assert.Equal("gpt-5.6-terra", Assert.Single(sample.Usage!).Model);
                Assert.Equal(1_000, sample.CompletedDurationMs);
                Assert.True(sample.DurationComplete);
            }
            if (mode == "foreignMetadata")
                Assert.StartsWith("Native agent metadata is unavailable.", sample.Error, StringComparison.Ordinal);

            Assert.Same(sample, Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None)));
            Assert.Equal(1, stops);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Own metadata and historical usage stay distinct; cache hits make no extra native requests.</summary>
    [Fact]
    public async Task ReadsExactMetadataUsageAndDurationsWithoutTakingTaskOwnership()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            var command = await CreateServerAsync(root, "ready");
            using var source = new CodexAppServerTelemetrySource(TimeProvider.System, command);
            var result = Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None));
            Assert.Equal("gpt-6.1-sol", result.Configuration?.Model);
            Assert.Equal("xhigh", result.Configuration?.ReasoningEffort);
            Assert.Equal("parent-a", result.ParentThreadId);
            var group = Assert.Single(result.Usage!);
            Assert.Equal("gpt-5.6-terra", group.Model);
            Assert.Equal(0, group.CachedInputTokens);
            Assert.Null(group.OutputTokens);
            Assert.Equal(1_000, result.CompletedDurationMs);
            Assert.Equal(1, result.CompletedTurnCount);
            Assert.True(result.DurationComplete);
            var cached = Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None));
            Assert.Same(result, cached);
            var requests = (await File.ReadAllLinesAsync(Path.Combine(root, "requests.jsonl"))).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                Assert.Equal(ExpectedMethods,
                    requests.Select(item => item.RootElement.GetProperty("method").GetString()));
                Assert.False(requests[2].RootElement.GetProperty("params").GetProperty("includeTurns").GetBoolean());
                Assert.Equal("notLoaded", requests[4].RootElement.GetProperty("params").GetProperty("itemsView").GetString());
            }
            finally { foreach (var request in requests) request.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Foreign metadata never labels an agent, and independent unavailable usage does not hide its own model.</summary>
    [Theory]
    [InlineData("foreignMetadata", false, false)]
    [InlineData("foreignUsage", true, false)]
    [InlineData("missingUsage", true, false)]
    [InlineData("negative", true, false)]
    [InlineData("oversized", false, false)]
    public async Task InvalidOrMissingSourcesStayUnavailable(string mode, bool configuration, bool usage)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            using var source = new CodexAppServerTelemetrySource(TimeProvider.System, await CreateServerAsync(root, mode));
            var sample = Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None));
            Assert.Equal(configuration, sample.Configuration is not null);
            Assert.Equal(usage, sample.Usage is not null);
            Assert.NotNull(sample.Error);
            Assert.DoesNotContain("private", sample.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Overlapping pages and a repeated native cursor cannot multiply completed durations.</summary>
    [Fact]
    public async Task RepeatedTurnCursorIsBoundedAndDurationCoverageIsPartial()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = CreateRoot();
        try
        {
            using var source = new CodexAppServerTelemetrySource(TimeProvider.System, await CreateServerAsync(root, "cursor"));
            var sample = Assert.Single(await source.ReadAsync(["task-a"], CancellationToken.None));
            Assert.Equal(1_000, sample.CompletedDurationMs);
            Assert.Equal(1, sample.CompletedTurnCount);
            Assert.False(sample.DurationComplete);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-telemetry-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> CreateServerAsync(string root, string mode)
    {
        var script = $$$"""
            $ErrorActionPreference = 'Stop'
            $mode = '{{{mode}}}'
            while ($null -ne ($line = [Console]::ReadLine())) {
              Add-Content -LiteralPath (Join-Path $PSScriptRoot 'requests.jsonl') -Value $line
              $request = $line | ConvertFrom-Json
              if ($request.method -eq 'initialized') { continue }
              $result = @{}
              switch ($request.method) {
                'thread/read' {
                  if ($mode -eq 'oversized') { [Console]::WriteLine('x' * 150000); continue }
                  $identity = if ($mode -eq 'foreignMetadata') { 'foreign-private-task' } else { $request.params.threadId }
                  $result = @{thread=@{id=$identity;parentThreadId='parent-a';model='gpt-6.1-sol';reasoningEffort='xhigh';ephemeral=$false}}
                }
                'account/usage/read' {
                  $identity = if ($mode -eq 'foreignUsage') { 'foreign-private-task' } else { $request.params.threadId }
                  $inputTokens = if ($mode -eq 'negative') { -1 } else { 10 }
                  $usage = @{threadId=$identity;groups=@(@{model='gpt-5.6-terra';reasoningEffort='low';inputTokens=$inputTokens;cachedInputTokens=0;outputTokens=$null;totalTokens=10})}
                  if ($mode -eq 'missingUsage') { $usage = $null }
                  $result = @{threadUsage=$usage}
                }
                'thread/turns/list' {
                  $cursor = if ($mode -eq 'cursor') { 'same' } else { $null }
                  $result = @{data=@(@{id='turn-a';status='completed';durationMs=1000});nextCursor=$cursor}
                }
              }
              [Console]::WriteLine((@{id=$request.id;result=$result} | ConvertTo-Json -Compress -Depth 10))
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(root, "server.ps1"), script);
        var command = Path.Combine(root, "server.cmd");
        await File.WriteAllTextAsync(command, "@echo off\r\npwsh.exe -NoProfile -File \"%~dp0server.ps1\"\r\n");
        return command;
    }
}
