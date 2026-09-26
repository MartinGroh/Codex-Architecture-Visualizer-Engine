using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.Workspaces;

namespace Cave.Tests;

/// <summary>
/// Verifies the machine-local workspace catalog and its activity overview projection.
/// </summary>
public sealed class MachineWorkspaceCatalogStoreTests
{
    /// <summary>
    /// Pins the cross-language Windows workspace identity contract used by hooks and .NET hosts.
    /// </summary>
    [Fact]
    public void WorkspaceIdentityMatchesHookContract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(
            "31156ac323b2a0f270416368",
            MachineWorkspaceCatalogStore.CreateWorkspaceId(@"C:\Code\Example"));
    }

    /// <summary>
    /// Verifies that registration is stable, atomic, and refreshes one canonical record.
    /// </summary>
    [Fact]
    public async Task RegisterRefreshesOneStableWorkspaceRecord()
    {
        var testRoot = CreateTestRoot();
        var workspaceRoot = Path.Combine(testRoot, "Workspace");
        var catalogRoot = Path.Combine(testRoot, "catalog");
        Directory.CreateDirectory(workspaceRoot);
        var clock = new MutableCatalogTimeProvider(
            new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

        try
        {
            var store = new MachineWorkspaceCatalogStore(catalogRoot, clock);
            var first = await store.RegisterAsync(workspaceRoot, CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(2));
            var second = await store.RegisterAsync(workspaceRoot + Path.DirectorySeparatorChar, CancellationToken.None);
            var catalog = await store.ReadAsync(CancellationToken.None);

            Assert.Equal(first.WorkspaceId, second.WorkspaceId);
            Assert.Equal(clock.GetUtcNow(), second.LastSeenAtUtc);
            Assert.Equal(second, Assert.Single(catalog.Entries));
            Assert.Empty(catalog.Errors);
            Assert.Single(Directory.EnumerateFiles(catalogRoot, "*.json"));
            Assert.Empty(Directory.EnumerateFiles(catalogRoot, "*.tmp"));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that invalid machine records are visible without hiding valid projects.
    /// </summary>
    [Fact]
    public async Task ReadReportsInvalidRecordsAlongsideValidEntries()
    {
        var testRoot = CreateTestRoot();
        var workspaceRoot = Path.Combine(testRoot, "Workspace");
        var catalogRoot = Path.Combine(testRoot, "catalog");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var store = new MachineWorkspaceCatalogStore(catalogRoot, TimeProvider.System);
            var valid = await store.RegisterAsync(workspaceRoot, CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(catalogRoot, "broken.json"),
                "{not-json",
                CancellationToken.None);

            var catalog = await store.ReadAsync(CancellationToken.None);

            Assert.Equal(valid, Assert.Single(catalog.Entries));
            Assert.Single(catalog.Errors);
            Assert.Contains("broken.json", catalog.Errors[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that the dashboard summary reuses the canonical activity reducer.
    /// </summary>
    [Fact]
    public async Task OverviewReportsFreshActiveAgentState()
    {
        var testRoot = CreateTestRoot();
        var workspaceRoot = Path.Combine(testRoot, "Workspace");
        var catalogRoot = Path.Combine(testRoot, "catalog");
        CreateWorkspaceIndex(workspaceRoot);
        var now = new DateTimeOffset(2026, 8, 17, 11, 0, 0, TimeSpan.Zero);
        var clock = new MutableCatalogTimeProvider(now);

        try
        {
            var catalogStore = new MachineWorkspaceCatalogStore(catalogRoot, clock);
            await catalogStore.RegisterAsync(workspaceRoot, CancellationToken.None);
            var activityStore = new FileAgentActivityStore(clock);
            await activityStore.AppendScopeAsync(
                workspaceRoot,
                new AgentScopeDeclaration(
                    "subagent-1",
                    "implementation",
                    IsSubagent: true,
                    AgentWorkState.Active,
                    "Building the catalog",
                    [],
                    [],
                    [],
                    []),
                CancellationToken.None);
            var service = new WorkspaceCatalogService(catalogStore, activityStore);

            var snapshot = await service.GetOverviewAsync(CancellationToken.None);
            var workspace = Assert.Single(snapshot.Workspaces);

            Assert.Equal(Environment.MachineName, snapshot.HostName);
            Assert.True(workspace.IsAvailable);
            Assert.Equal(1, workspace.ActiveAgentCount);
            Assert.Equal(1, workspace.ActiveSubagentCount);
            Assert.Equal(AgentActivityPhase.Working, workspace.CurrentPhase);
            Assert.Equal("Building the catalog", workspace.ActivitySummary);
            Assert.Empty(snapshot.Errors);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that hook-observed global roots do not masquerade as initialized projects.
    /// </summary>
    [Fact]
    public async Task OverviewOmitsExistingRootsWithoutAWorkspaceIndex()
    {
        var testRoot = CreateTestRoot();
        var incidentalRoot = Path.Combine(testRoot, "UserProfile");
        var projectRoot = Path.Combine(testRoot, "Project");
        var catalogRoot = Path.Combine(testRoot, "catalog");
        Directory.CreateDirectory(Path.Combine(incidentalRoot, ".codegraph", "daemons"));
        CreateWorkspaceIndex(projectRoot);

        try
        {
            var catalogStore = new MachineWorkspaceCatalogStore(catalogRoot, TimeProvider.System);
            var incidental = await catalogStore.RegisterAsync(incidentalRoot, CancellationToken.None);
            var project = await catalogStore.RegisterAsync(projectRoot, CancellationToken.None);
            var service = new WorkspaceCatalogService(
                catalogStore,
                new FileAgentActivityStore(TimeProvider.System));

            var snapshot = await service.GetOverviewAsync(CancellationToken.None);

            Assert.Equal(project.WorkspaceId, Assert.Single(snapshot.Workspaces).WorkspaceId);
            Assert.Null(await service.FindAsync(incidental.WorkspaceId, CancellationToken.None));
            Assert.Equal(project, await service.FindAsync(project.WorkspaceId, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a missing registration remains visible for operator diagnosis.
    /// </summary>
    [Fact]
    public async Task OverviewKeepsMissingRegisteredWorkspaceVisible()
    {
        var testRoot = CreateTestRoot();
        var workspaceRoot = Path.Combine(testRoot, "Project");
        var catalogRoot = Path.Combine(testRoot, "catalog");
        CreateWorkspaceIndex(workspaceRoot);

        try
        {
            var catalogStore = new MachineWorkspaceCatalogStore(catalogRoot, TimeProvider.System);
            var registered = await catalogStore.RegisterAsync(workspaceRoot, CancellationToken.None);
            Directory.Delete(workspaceRoot, recursive: true);
            var service = new WorkspaceCatalogService(
                catalogStore,
                new FileAgentActivityStore(TimeProvider.System));

            var snapshot = await service.GetOverviewAsync(CancellationToken.None);
            var workspace = Assert.Single(snapshot.Workspaces);

            Assert.Equal(registered.WorkspaceId, workspace.WorkspaceId);
            Assert.False(workspace.IsAvailable);
            Assert.Equal(registered, await service.FindAsync(registered.WorkspaceId, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "cave-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateWorkspaceIndex(string workspaceRoot)
    {
        var codeGraphRoot = Path.Combine(workspaceRoot, ".codegraph");
        Directory.CreateDirectory(codeGraphRoot);
        File.WriteAllBytes(Path.Combine(codeGraphRoot, "codegraph.db"), []);
    }

    private sealed class MutableCatalogTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
