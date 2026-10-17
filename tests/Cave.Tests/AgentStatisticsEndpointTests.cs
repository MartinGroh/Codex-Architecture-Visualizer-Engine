using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cave.Tests;

/// <summary>Verifies opaque task/project scopes, explicit synthetic data and transport privacy.</summary>
public sealed class AgentStatisticsEndpointTests
{
    /// <summary>Overall, project and task read views agree without exposing native identities or root paths.</summary>
    [Fact]
    public async Task ScopedStatisticsAgreeAndKeepPrivateCollectionKeysInternal()
    {
        var root = Path.Combine(Path.GetTempPath(), "cave-statistics-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".codegraph"));
        await File.WriteAllTextAsync(Path.Combine(root, ".codegraph", "codegraph.db"), string.Empty);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
                .UseSetting("Cave:DemoMode", "true").UseSetting("Cave:WorkspaceRoot", root)
                .UseSetting("Cave:WorkspaceCatalogRoot", Path.Combine(root, "catalog")));
            using var client = factory.CreateClient();
            var body = await client.GetStringAsync("/api/statistics");
            using var global = JsonDocument.Parse(body);
            Assert.True(global.RootElement.GetProperty("isSynthetic").GetBoolean());
            Assert.Equal(1, global.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(4, global.RootElement.GetProperty("totals").GetProperty("agentCount").GetInt32());
            Assert.Equal(3, global.RootElement.GetProperty("totals").GetProperty("subagentCount").GetInt32());
            Assert.Equal(3, global.RootElement.GetProperty("totals").GetProperty("reportingAgents").GetInt32());
            Assert.False(global.RootElement.GetProperty("totals").GetProperty("tokenCoverageComplete").GetBoolean());
            Assert.DoesNotContain("demo-main", body, StringComparison.Ordinal);
            Assert.DoesNotContain(root, body, StringComparison.Ordinal);
            Assert.Equal(4, global.RootElement.GetProperty("families").GetArrayLength());
            var taskId = global.RootElement.GetProperty("tasks")[0].GetProperty("id").GetString();
            var workspaceId = global.RootElement.GetProperty("projects")[0].GetProperty("id").GetString();
            using var project = JsonDocument.Parse(await client.GetStringAsync($"/api/statistics?workspace={workspaceId}"));
            using var task = JsonDocument.Parse(await client.GetStringAsync($"/api/statistics?workspace={workspaceId}&task={taskId}"));
            Assert.Equal("Project", project.RootElement.GetProperty("scope").GetString());
            Assert.Equal("Task", task.RootElement.GetProperty("scope").GetString());
            Assert.Equal(global.RootElement.GetProperty("totals").GetRawText(), task.RootElement.GetProperty("totals").GetRawText());
            using var invalid = await client.GetAsync("/api/statistics?task=native-task-id");
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
            using var missing = await client.GetAsync("/api/statistics?workspace=not-registered");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
