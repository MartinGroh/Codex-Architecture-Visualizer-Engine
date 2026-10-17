using System.ComponentModel;
using System.Text.Json;
using Cave.Application;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cave.Mcp;

/// <summary>Exposes the same retained statistics projection to the embedded app without native task control.</summary>
[McpServerToolType]
public sealed class StatisticsTools(AgentTelemetryService telemetry, IWorkspaceCatalogStore catalog)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>Reads statistics only for the app's registered workspace and optional opaque task key.</summary>
    /// <param name="workspacePath">The exact initialized workspace supplied by the owning architecture app.</param>
    /// <param name="taskId">An optional opaque main-task statistics key.</param>
    /// <param name="cancellationToken">Signals that the retained read should stop.</param>
    [McpServerTool(Name = "cave_get_statistics", Title = "Read agent statistics", ReadOnly = true)]
    [McpAppUi(Visibility = [McpUiToolVisibility.App])]
    [Description("Read retained model, reasoning effort, token coverage and observed agent-time statistics for the current workspace.")]
    public async Task<CallToolResult> GetAsync(string workspacePath, string? taskId, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(workspacePath);
        var entries = await catalog.ReadAsync(cancellationToken).ConfigureAwait(false);
        var workspace = entries.Entries.FirstOrDefault(entry => Path.GetFullPath(entry.WorkspaceRoot).Equals(root,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            ?? throw new ArgumentException("The statistics workspace is not registered.", nameof(workspacePath));
        var names = entries.Entries.ToDictionary(entry => entry.WorkspaceId, entry => new DirectoryInfo(entry.WorkspaceRoot).Name, StringComparer.Ordinal);
        var statistics = await telemetry.ReadStatisticsAsync(workspace.WorkspaceId, taskId, names, cancellationToken).ConfigureAwait(false);
        return new() { Content = [new TextContentBlock { Text = "Loaded retained agent usage statistics." }],
            StructuredContent = JsonSerializer.SerializeToElement(statistics, JsonOptions) };
    }
}
