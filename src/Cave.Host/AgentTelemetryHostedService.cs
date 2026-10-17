using Cave.Application;

/// <summary>Collects retained telemetry independently of graph, HTTP and MCP request latency.</summary>
internal sealed partial class AgentTelemetryHostedService(AgentTelemetryService telemetry, WorkspaceCatalogService catalog,
    ILogger<AgentTelemetryHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var overview = await catalog.GetOverviewAsync(stoppingToken).ConfigureAwait(false);
                var workspaces = overview.Workspaces.Where(workspace => workspace.IsAvailable)
                    .Select(workspace => new WorkspaceCatalogEntry(workspace.WorkspaceId, workspace.RootPath, workspace.LastSeenAtUtc)).ToArray();
                await telemetry.CollectAsync(workspaces, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            { CollectionUnavailable(logger, exception.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent statistics collection is unavailable ({FailureType}).")]
    private static partial void CollectionUnavailable(ILogger logger, string failureType);
}
