using System.Text.Json;
using System.Globalization;
using System.Net.Http.Json;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Workspaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cave.Tests;

/// <summary>
/// Verifies the composed ASP.NET Core host surface.
/// </summary>
/// <param name="factory">The in-memory CAVE host factory.</param>
public sealed class HostSnapshotTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _ownedFactories = [];
    private readonly string _workspaceRoot = CreateWorkspaceRoot();
    private readonly string _catalogRoot = Path.Combine(
        Path.GetTempPath(),
        "cave-host-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that the health endpoint reports a healthy host.
    /// </summary>
    [Fact]
    public async Task HealthEndpointIsHealthy()
    {
        using var client = CreateSampleFactory().CreateClient();

        using var response = await client.GetAsync("/health", CancellationToken.None);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Verifies that MCP launchers can reject stale or incompatible machine viewers.
    /// </summary>
    [Fact]
    public async Task RuntimeEndpointPublishesViewerCompatibility()
    {
        using var client = CreateSampleFactory().CreateClient();

        using var response = await client.GetAsync("/api/runtime", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));

        Assert.Equal(
            Cave.Application.CaveRuntimeContract.ViewerProtocolVersion,
            payload.RootElement.GetProperty("viewerProtocolVersion").GetInt32());
    }

    /// <summary>
    /// Verifies that the read-only information endpoint exposes usage through the application port.
    /// </summary>
    [Fact]
    public async Task InfoEndpointPublishesCodexUsage()
    {
        using var client = CreateSampleFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICodexUsageProvider>();
                services.AddSingleton<ICodexUsageProvider>(new StubUsageProvider());
            })).CreateClient();

        using var response = await client.GetAsync("/api/info", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));

        Assert.Equal("Ready", payload.RootElement.GetProperty("usage").GetProperty("status").GetString());
        Assert.Equal(
            1234,
            payload.RootElement.GetProperty("usage").GetProperty("summary").GetProperty("lifetimeTokens").GetInt64());
    }

    /// <summary>
    /// Verifies that the trusted-network browser can opt a selected workspace into conversation sharing.
    /// </summary>
    [Fact]
    public async Task ConversationSharingEndpointControlsSelectedWorkspace()
    {
        using var client = CreateSampleFactory().CreateClient();
        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);

        using var infoPost = await client.PostAsync("/api/info", content: null, CancellationToken.None);
        using var conversationPost = await client.PostAsJsonAsync(
            $"/api/conversation/sharing?workspace={workspaceId}",
            new { enabled = true },
            CancellationToken.None);

        Assert.False(infoPost.IsSuccessStatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, conversationPost.StatusCode);

        var conversation = await new Cave.Infrastructure.Conversation.FileConversationStore()
            .ReadAsync(_workspaceRoot, CancellationToken.None);
        Assert.True(conversation.SharingEnabled);
    }

    /// <summary>
    /// Verifies that browser chat preserves the workspace and exact Codex task identity at the HTTP boundary.
    /// </summary>
    [Fact]
    public async Task ConversationMessageEndpointQueuesAgainstExactTask()
    {
        var control = new StubConversationControl();
        using var client = CreateSampleFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConversationControl>();
                services.AddSingleton<IConversationControl>(control);
            })).CreateClient();
        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);

        using var response = await client.PostAsJsonAsync(
            $"/api/conversation/messages?workspace={workspaceId}",
            new { expectedSessionId = "session-exact", text = "Continue from the browser" },
            CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(Path.GetFullPath(_workspaceRoot), control.WorkspaceRoot);
        Assert.Equal("session-exact", control.ExpectedSessionId);
        Assert.Equal("Continue from the browser", control.Text);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal("Queued", payload.RootElement.GetProperty("state").GetString());
    }

    /// <summary>
    /// Verifies that a node question preserves workspace and task identity and returns only its modal answer.
    /// </summary>
    [Fact]
    public async Task NodeMemoEndpointRunsTemporarySideChatAgainstExactTask()
    {
        var control = new StubConversationControl();
        using var client = CreateSampleFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConversationControl>();
                services.AddSingleton<IConversationControl>(control);
            })).CreateClient();
        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);

        using var response = await client.PostAsJsonAsync(
            $"/api/conversation/node-memos?workspace={workspaceId}",
            new { expectedSessionId = "session-exact", text = "Explain Cave.Application" },
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.Equal(Path.GetFullPath(_workspaceRoot), control.WorkspaceRoot);
        Assert.Equal("session-exact", control.ExpectedSessionId);
        Assert.Equal("Explain Cave.Application", control.Text);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal("Temporary memo answer.", payload.RootElement.GetProperty("text").GetString());
    }

    /// <summary>
    /// Verifies that the standalone root can discover the configured project through the machine catalog.
    /// </summary>
    [Fact]
    public async Task WorkspaceEndpointListsConfiguredProject()
    {
        using var client = CreateSampleFactory().CreateClient();

        using var response = await client.GetAsync("/api/workspaces", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));
        var workspace = Assert.Single(payload.RootElement.GetProperty("workspaces").EnumerateArray());

        Assert.Equal(Environment.MachineName, payload.RootElement.GetProperty("hostName").GetString());
        Assert.Equal(
            MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot),
            workspace.GetProperty("workspaceId").GetString());
        Assert.True(workspace.GetProperty("isAvailable").GetBoolean());
    }

    /// <summary>
    /// Verifies recent activity returns hook metadata and declared evidence without journal payloads.
    /// </summary>
    [Fact]
    public async Task RecentActivityEndpointPublishesSafeHookEventAndWorkspaceBinding()
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var activityDirectory = Cave.Infrastructure.Activity.FileAgentActivityStore.GetActivityDirectory(_workspaceRoot);
        Directory.CreateDirectory(activityDirectory);
        var fileTimestamp = occurredAtUtc.UtcDateTime.ToString("yyyyMMddHHmmssffffff", CultureInfo.InvariantCulture);
        var journalEvent = new
        {
            schemaVersion = 1,
            eventId = "recent-event-1",
            kind = "PreToolUse",
            occurredAtUtc,
            sessionId = "session-1",
            turnId = "turn-1",
            agentId = "agent-1",
            agentType = "implementation",
            isSubagent = false,
            toolName = "apply_patch",
            phase = "Editing",
            summary = "private prompt text must not be returned",
            paths = new[] { "private/file/path.cs" },
        };
        await File.WriteAllTextAsync(
            Path.Combine(activityDirectory, $"{fileTimestamp}-recent-event-1.json"),
            JsonSerializer.Serialize(journalEvent));
        await new Cave.Infrastructure.Activity.FileAgentActivityStore(TimeProvider.System).AppendScopeAsync(
            _workspaceRoot,
            new AgentScopeDeclaration(
                "agent-declared",
                "implementation",
                IsSubagent: false,
                AgentWorkState.Active,
                "another private prompt must not be returned",
                [],
                [],
                [],
                []),
            CancellationToken.None);

        using var client = CreateSampleFactory().CreateClient();
        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);
        using var response = await client.GetAsync(
            $"/api/recent-activity?minutes=15&workspace={workspaceId}",
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        var responseText = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using var payload = JsonDocument.Parse(responseText);
        var root = payload.RootElement;
        var workspace = Assert.Single(root.GetProperty("registeredWorkspaces").EnumerateArray());
        var activityEvents = root.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(2, activityEvents.Length);
        var activityEvent = activityEvents.Single(item => item.GetProperty("eventId").GetString() == "recent-event-1");
        var declaredEvent = activityEvents.Single(item => item.GetProperty("action").GetString() == "Scope declared");

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("workspace", root.GetProperty("scope").GetProperty("kind").GetString());
        Assert.Equal(workspaceId, workspace.GetProperty("workspaceId").GetString());
        Assert.Equal(_workspaceRoot, workspace.GetProperty("rootPath").GetString());
        Assert.Equal("recent-event-1", activityEvent.GetProperty("eventId").GetString());
        Assert.Equal("Started apply_patch", activityEvent.GetProperty("action").GetString());
        Assert.Equal("Editing", activityEvent.GetProperty("phase").GetString());
        Assert.Equal("observed", activityEvent.GetProperty("evidence").GetString());
        Assert.Equal(workspaceId,
            Assert.Single(activityEvent.GetProperty("memberships").EnumerateArray())
                .GetProperty("workspaceId").GetString());
        Assert.Equal("declared", declaredEvent.GetProperty("evidence").GetString());
        Assert.DoesNotContain("private prompt text", responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("another private prompt", responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("private/file/path.cs", responseText, StringComparison.Ordinal);
        Assert.False(activityEvent.TryGetProperty("summary", out _));
    }

    /// <summary>
    /// Verifies the VPN-oriented read-only catalog no longer issues an authentication challenge.
    /// </summary>
    [Fact]
    public async Task WorkspaceEndpointDoesNotRequireLanToken()
    {
        using var client = CreateSampleFactory().CreateClient();

        using var response = await client.GetAsync("/api/workspaces", CancellationToken.None);

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
    }

    /// <summary>
    /// Verifies that a published machine host does not treat an installation ancestor as a project.
    /// </summary>
    [Fact]
    public async Task WorkspaceEndpointDoesNotInferAProjectFromHostLocation()
    {
        using var client = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Cave:SemanticProvider", "Sample")
            .UseSetting("Cave:WorkspaceCatalogRoot", Path.Combine(_catalogRoot, "unscoped")))
            .CreateClient();

        using var response = await client.GetAsync("/api/workspaces", CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));

        Assert.Empty(payload.RootElement.GetProperty("workspaces").EnumerateArray());
    }

    /// <summary>
    /// Verifies that browser graph requests cannot silently select an arbitrary default workspace.
    /// </summary>
    [Fact]
    public async Task SnapshotEndpointRequiresWorkspaceSelection()
    {
        using var client = CreateSampleFactory().CreateClient();

        using var response = await client.GetAsync("/api/snapshot", CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that the acceptance snapshot is explicit about its non-live sample source.
    /// </summary>
    [Fact]
    public async Task SnapshotEndpointLabelsSampleEvidence()
    {
        using var client = CreateSampleFactory().CreateClient();

        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);
        using var response = await client.GetAsync(
            $"/api/snapshot?workspace={workspaceId}",
            CancellationToken.None);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(CancellationToken.None));

        var snapshot = payload.RootElement.GetProperty("snapshot");
        var metadata = snapshot.GetProperty("metadata");
        Assert.Equal("Sample", metadata.GetProperty("sourceKind").GetString());
        Assert.False(metadata.GetProperty("isLive").GetBoolean());
        Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(9, snapshot.GetProperty("graph").GetProperty("nodes").GetArrayLength());
        var git = snapshot.GetProperty("git");
        Assert.Equal("Ready", git.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            git.GetProperty("baseline").GetProperty("resolvedSha").GetString()));
    }

    /// <summary>
    /// Verifies that explicit demo mode replaces every potentially private evidence source.
    /// </summary>
    [Fact]
    public async Task DemoModePublishesOnlySyntheticEvidence()
    {
        using var client = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Cave:DemoMode", "true")
            .UseSetting("Cave:WorkspaceRoot", _workspaceRoot)
            .UseSetting("Cave:WorkspaceCatalogRoot", Path.Combine(_catalogRoot, "demo")))
            .CreateClient();

        var workspaceId = MachineWorkspaceCatalogStore.CreateWorkspaceId(_workspaceRoot);
        using var snapshotResponse = await client.GetAsync(
            $"/api/snapshot?workspace={workspaceId}",
            CancellationToken.None);
        snapshotResponse.EnsureSuccessStatusCode();
        using var snapshotPayload = JsonDocument.Parse(
            await snapshotResponse.Content.ReadAsStreamAsync(CancellationToken.None));
        var snapshot = snapshotPayload.RootElement.GetProperty("snapshot");

        Assert.Equal("cave-demo-1", snapshot.GetProperty("metadata").GetProperty("providerId").GetString());
        Assert.Equal(22, snapshot.GetProperty("graph").GetProperty("nodes").GetArrayLength());
        Assert.Equal(3, snapshot.GetProperty("activity").GetProperty("agents").GetArrayLength());
        Assert.True(snapshot.GetProperty("conversation").GetProperty("sharingEnabled").GetBoolean());
        Assert.Equal(2, snapshot.GetProperty("conversation").GetProperty("messages").GetArrayLength());
        Assert.Equal("feature/semantic-focus", snapshot.GetProperty("git").GetProperty("worktree").GetProperty("branch").GetString());

        using var infoResponse = await client.GetAsync("/api/info", CancellationToken.None);
        infoResponse.EnsureSuccessStatusCode();
        using var infoPayload = JsonDocument.Parse(
            await infoResponse.Content.ReadAsStreamAsync(CancellationToken.None));
        Assert.Equal(
            8_420_000,
            infoPayload.RootElement.GetProperty("usage").GetProperty("summary").GetProperty("lifetimeTokens").GetInt64());
    }

    private WebApplicationFactory<Program> CreateSampleFactory()
    {
        var ownedFactory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Cave:SemanticProvider", "Sample")
            .UseSetting("Cave:WorkspaceRoot", _workspaceRoot)
            .UseSetting("Cave:WorkspaceCatalogRoot", _catalogRoot));
        _ownedFactories.Add(ownedFactory);
        return ownedFactory;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var ownedFactory in _ownedFactories)
        {
            ownedFactory.Dispose();
        }

        if (Directory.Exists(_catalogRoot))
        {
            Directory.Delete(_catalogRoot, recursive: true);
        }

        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private static string CreateWorkspaceRoot()
    {
        var workspaceRoot = Path.Combine(
            AppContext.BaseDirectory,
            "workspace-fixtures",
            Guid.NewGuid().ToString("N"),
            "Workspace");
        var codeGraphRoot = Path.Combine(workspaceRoot, ".codegraph");
        Directory.CreateDirectory(codeGraphRoot);
        File.WriteAllBytes(Path.Combine(codeGraphRoot, "codegraph.db"), []);
        return workspaceRoot;
    }

    private sealed class StubUsageProvider : ICodexUsageProvider
    {
        public Task<CodexUsageSnapshot> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CodexUsageSnapshot(
                CodexUsageStatus.Ready,
                new CodexUsageSummary(1234, 100, 60, 2, 5),
                [new CodexDailyUsage(new DateOnly(2026, 8, 19), 100)],
                [],
                new DateTimeOffset(2026, 8, 19, 18, 0, 0, TimeSpan.Zero),
                null));
    }

    private sealed class StubConversationControl : IConversationControl
    {
        public string? WorkspaceRoot { get; private set; }

        public string? ExpectedSessionId { get; private set; }

        public string? Text { get; private set; }

        public Task<ConversationDelivery> QueueAsync(
            string workspaceRoot,
            string expectedSessionId,
            string text,
            CancellationToken cancellationToken)
        {
            WorkspaceRoot = workspaceRoot;
            ExpectedSessionId = expectedSessionId;
            Text = text;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ConversationDelivery(
                "message-1",
                expectedSessionId,
                TurnId: null,
                ConversationDeliveryState.Queued,
                now,
                now,
                Error: null));
        }

        public Task<string> RunNodeMemoAsync(
            string workspaceRoot,
            string expectedSessionId,
            string text,
            CancellationToken cancellationToken)
        {
            WorkspaceRoot = workspaceRoot;
            ExpectedSessionId = expectedSessionId;
            Text = text;
            return Task.FromResult("Temporary memo answer.");
        }
    }
}
