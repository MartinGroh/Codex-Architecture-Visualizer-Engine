using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Live;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cave.Mcp;

/// <summary>
/// Exposes the CAVE architecture view and its app-private live polling operation.
/// </summary>
[McpServerToolType]
public sealed class CaveTools(
    WorkspaceGraphMonitorRegistry registry,
    IAgentActivityStore activityStore,
    IConversationStore conversationStore,
    CaveInfoService infoService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    static CaveTools()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Opens the live CAVE architecture graph for a workspace.
    /// </summary>
    /// <param name="workspacePath">The absolute workspace or repository root.</param>
    /// <param name="cancellationToken">Signals that the request should stop.</param>
    /// <returns>The initial graph and an opaque live subscription.</returns>
    [McpServerTool(
        Name = "cave_show_architecture",
        Title = "Show live workspace architecture",
        ReadOnly = true)]
    [McpAppUi(
        ResourceUri = CaveResources.ArchitectureAppUri,
        Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
    [Description("Open CAVE's interactive architecture graph for an absolute workspace path. The graph stays synchronized with source-file changes.")]
    public async Task<CallToolResult> ShowArchitectureAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        CancellationToken cancellationToken)
    {
        var session = await registry.GetOrCreateAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var update = await session.Monitor.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return CreateResult(
            new CaveToolResult(session.SubscriptionId, session.Monitor.WorkspaceRoot, update),
            $"Opened the live CAVE graph for '{update.Snapshot.Metadata.WorkspaceName}' at version {update.Version}.");
    }

    /// <summary>
    /// Waits briefly for a newer live graph version.
    /// </summary>
    /// <param name="subscriptionId">The subscription returned by the show tool.</param>
    /// <param name="afterVersion">The latest version already rendered by the app.</param>
    /// <param name="cancellationToken">Signals that the poll should stop.</param>
    /// <returns>The latest graph version.</returns>
    [McpServerTool(
        Name = "cave_poll_architecture",
        Title = "Poll live architecture",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.App])]
    [Description("App-private long poll for the next CAVE architecture graph version.")]
    public async Task<CallToolResult> PollArchitectureAsync(
        [Description("Opaque subscription returned by cave_show_architecture.")] string subscriptionId,
        [Description("Latest graph version already rendered by the app.")] long afterVersion,
        CancellationToken cancellationToken)
    {
        var session = registry.GetBySubscription(subscriptionId);
        var update = await session.Monitor.WaitForUpdateAsync(
            afterVersion,
            TimeSpan.FromSeconds(18),
            cancellationToken).ConfigureAwait(false);
        return CreateResult(
            new CaveToolResult(subscriptionId, session.Monitor.WorkspaceRoot, update),
            $"CAVE graph version {update.Version}.");
    }

    /// <summary>
    /// Declares the exact architecture scope and lifecycle state of an agent or subagent.
    /// </summary>
    /// <param name="workspacePath">The absolute workspace or repository root.</param>
    /// <param name="agentId">A stable identifier for the declaring agent.</param>
    /// <param name="state">The declared work lifecycle state.</param>
    /// <param name="summary">A concise description of the current work.</param>
    /// <param name="agentType">The agent role or type.</param>
    /// <param name="isSubagent">Whether the declaration belongs to a child agent.</param>
    /// <param name="projects">Exact project ids, names, or qualified names.</param>
    /// <param name="namespaces">Exact namespace ids, names, or qualified names.</param>
    /// <param name="classes">Exact class, interface, or abstract-class ids, names, or qualified names.</param>
    /// <param name="files">Workspace-relative files in scope.</param>
    /// <param name="cancellationToken">Signals that the declaration should stop.</param>
    /// <returns>The graph version that includes the declaration.</returns>
    [McpServerTool(
        Name = "cave_set_scope",
        Title = "Declare agent architecture scope",
        ReadOnly = false)]
    [Description("Record an agent's planned, active, idle, or completed architecture scope. Exact scope evidence remains distinct from observed Codex hook activity.")]
    public async Task<CallToolResult> SetScopeAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("Stable id for the main agent or subagent.")] string agentId,
        [Description("Declared lifecycle state: Planned, Active, Idle, or Completed.")] AgentWorkState state,
        [Description("Concise description of what the agent is working on.")] string? summary = null,
        [Description("Agent role or type label.")] string agentType = "agent",
        [Description("Whether this identity is a child agent.")] bool isSubagent = false,
        [Description("Exact project ids, names, or qualified names.")] string[]? projects = null,
        [Description("Exact namespace ids, names, or qualified names.")] string[]? namespaces = null,
        [Description("Exact class, interface, or abstract-class ids, names, or qualified names.")] string[]? classes = null,
        [Description("Workspace-relative files in scope.")] string[]? files = null,
        CancellationToken cancellationToken = default)
    {
        var session = await registry.GetOrCreateAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var current = await session.Monitor.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var declaration = new AgentScopeDeclaration(
            agentId,
            agentType,
            isSubagent,
            state,
            summary,
            projects ?? [],
            namespaces ?? [],
            classes ?? [],
            files ?? []);
        await activityStore.AppendScopeAsync(session.Monitor.WorkspaceRoot, declaration, cancellationToken)
            .ConfigureAwait(false);
        var update = await session.Monitor.WaitForUpdateAsync(
            current.Version,
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);

        return CreateResult(
            new CaveToolResult(session.SubscriptionId, session.Monitor.WorkspaceRoot, update),
            $"Recorded {state} scope for agent '{agentId}' in CAVE graph version {update.Version}.");
    }

    /// <summary>
    /// Enables or disables opt-in public conversation sharing for a workspace.
    /// </summary>
    /// <param name="workspacePath">The absolute workspace or repository root.</param>
    /// <param name="enabled">Whether future public prompts and final replies may be retained.</param>
    /// <param name="cancellationToken">Signals that the request should stop.</param>
    /// <returns>The graph version that includes the sharing setting.</returns>
    [McpServerTool(
        Name = "cave_set_conversation_sharing",
        Title = "Set CAVE conversation sharing",
        ReadOnly = false)]
    [McpAppUi(Visibility = [McpUiToolVisibility.App])]
    [Description("Explicitly opt one workspace into or out of a bounded journal of public user prompts and final assistant replies. Tool content and reasoning are never retained.")]
    public async Task<CallToolResult> SetConversationSharingAsync(
        [Description("Absolute path to the workspace or repository root.")] string workspacePath,
        [Description("True to retain future public conversation messages; false to stop capture and hide retained messages.")] bool enabled,
        CancellationToken cancellationToken)
    {
        var session = await registry.GetOrCreateAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var current = await session.Monitor.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        await conversationStore.SetSharingAsync(
            session.Monitor.WorkspaceRoot,
            enabled,
            cancellationToken).ConfigureAwait(false);
        var update = await session.Monitor.WaitForUpdateAsync(
            current.Version,
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);

        return CreateResult(
            new CaveToolResult(session.SubscriptionId, session.Monitor.WorkspaceRoot, update),
            enabled
                ? "Enabled opt-in public conversation sharing for this workspace."
                : "Disabled public conversation sharing for this workspace.");
    }

    /// <summary>
    /// Gets CAVE runtime and Codex account usage information.
    /// </summary>
    /// <param name="cancellationToken">Signals that the request should stop.</param>
    /// <returns>The current application information.</returns>
    [McpServerTool(
        Name = "cave_get_info",
        Title = "Get CAVE information",
        ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.App])]
    [Description("Read CAVE runtime information and current Codex account usage statistics.")]
    public async Task<CallToolResult> GetInfoAsync(CancellationToken cancellationToken) =>
        CreateResult(
            await infoService.GetAsync(cancellationToken).ConfigureAwait(false),
            "Loaded CAVE information and Codex account usage.");

    private static CallToolResult CreateResult<T>(T result, string message) =>
        new()
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = JsonSerializer.SerializeToElement(result, JsonOptions),
        };
}

/// <summary>
/// Carries the live graph state shared between a CAVE MCP tool and its app.
/// </summary>
/// <param name="SubscriptionId">The opaque live subscription identifier.</param>
/// <param name="WorkspaceRoot">The canonical workspace used to reacquire a lost subscription.</param>
/// <param name="Update">The current versioned architecture graph.</param>
public sealed record CaveToolResult(
    string SubscriptionId,
    string WorkspaceRoot,
    LiveArchitectureSnapshot Update);
