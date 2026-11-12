using System.Text.Json;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Workspaces;
using Cave.Mcp;

namespace Cave.Tests;

/// <summary>Verifies local Dot declarations remain separate from Codex hook observations.</summary>
public sealed class DotScopeCommandTests
{
    private static readonly string[] DashboardFiles = ["src/Dashboard.cs"];

    /// <summary>Records a Dot scope without borrowing a concurrent Codex main task.</summary>
    [Fact]
    public async Task DeclarationHasItsOwnIdentityAndRegistersTheWorkspace()
    {
        var root = CreateWorkspace();
        var now = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        try
        {
            var journal = FileAgentActivityStore.GetActivityDirectory(root);
            Directory.CreateDirectory(journal);
            await File.WriteAllTextAsync(Path.Combine(journal, "202611121159590000000-prompt.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    eventId = "prompt",
                    kind = "UserPromptSubmit",
                    occurredAtUtc = now.AddSeconds(-1),
                    sessionId = "codex-task",
                    turnId = "codex-turn",
                    isSubagent = false,
                    workspaceRoot = root,
                    paths = Array.Empty<string>(),
                    summary = "New instruction received",
                }));

            var store = new FileAgentActivityStore(new FixedTimeProvider(now));
            var catalog = new MachineWorkspaceCatalogStore(
                Path.Combine(root, "catalog"), new FixedTimeProvider(now));
            using var input = new StringReader(JsonSerializer.Serialize(new
            {
                workspacePath = root,
                taskId = "rollout-42",
                state = "Active",
                summary = "Checking the dashboard",
                files = DashboardFiles,
            }));
            using var output = new StringWriter();
            using var error = new StringWriter();

            var result = await DotScopeCommand.RunAsync(
                input, output, error, store, catalog, CancellationToken.None);

            Assert.Equal(0, result);
            using var response = JsonDocument.Parse(output.ToString());
            Assert.Equal("dot:rollout-42", response.RootElement.GetProperty("agentId").GetString());
            Assert.False(response.RootElement.GetProperty("architectureIndexed").GetBoolean());
            Assert.Contains("/cave:init", error.ToString());
            Assert.Single((await catalog.ReadAsync(CancellationToken.None)).Entries);

            var agents = (await store.ReadAsync(root, ArchitectureGraph.Create([], []), CancellationToken.None)).Agents;
            var dot = Assert.Single(agents, agent => agent.AgentId == "dot:rollout-42");
            Assert.Equal("Dot", dot.AgentType);
            Assert.Equal(AgentWorkState.Active, dot.State);
            Assert.Equal("Checking the dashboard", dot.Summary);
            Assert.Equal(AgentActivityEvidenceKind.Declared, dot.Evidence);
            Assert.True(dot.HasDeclaredScope);
            Assert.False(dot.HasObservedActivity);
            var codex = Assert.Single(agents, agent => agent.AgentId == "session:codex-task");
            Assert.True(codex.HasObservedActivity);
            Assert.False(codex.HasDeclaredScope);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Rejects paths outside the worktree before writing activity or catalog records.</summary>
    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("/outside.cs")]
    public async Task RejectsEscapingFileScope(string file)
    {
        var root = CreateWorkspace();
        try
        {
            var store = new FileAgentActivityStore(TimeProvider.System);
            var catalog = new MachineWorkspaceCatalogStore(
                Path.Combine(root, "catalog"), TimeProvider.System);
            using var input = new StringReader(JsonSerializer.Serialize(new
            {
                workspacePath = root,
                taskId = "invalid",
                state = "Active",
                files = new[] { file },
            }));
            using var output = new StringWriter();
            using var error = new StringWriter();

            var result = await DotScopeCommand.RunAsync(
                input, output, error, store, catalog, CancellationToken.None);

            Assert.Equal(2, result);
            Assert.Empty(output.ToString());
            Assert.Contains("stay within", error.ToString());
            Assert.Empty((await catalog.ReadAsync(CancellationToken.None)).Entries);
            Assert.False(Directory.Exists(FileAgentActivityStore.GetActivityDirectory(root)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-dot-scope-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        return root;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
