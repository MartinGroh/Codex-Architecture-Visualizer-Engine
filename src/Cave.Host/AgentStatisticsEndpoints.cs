using Cave.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Maps read-only, opaque-scope statistics over already collected records.</summary>
internal static class AgentStatisticsEndpoints
{
    internal static IEndpointRouteBuilder MapAgentStatistics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/statistics", async Task<IResult> (string? workspace, string? task,
            HttpResponse response, WorkspaceCatalogService catalog, AgentTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            response.Headers.CacheControl = "no-store";
            if (task is not null && (task.Length != 64 || task.Any(character => !char.IsAsciiHexDigit(character))))
                return TypedResults.Problem(statusCode: 400, title: "An opaque task statistics key is required.");
            if (workspace is not null && await catalog.FindAsync(workspace, cancellationToken).ConfigureAwait(false) is null)
                return TypedResults.Problem(statusCode: 404, title: "The workspace is not registered.");
            var overview = await catalog.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
            var names = overview.Workspaces.ToDictionary(item => item.WorkspaceId, item => item.Name, StringComparer.Ordinal);
            return TypedResults.Ok(await telemetry.ReadStatisticsAsync(workspace, task, names, cancellationToken).ConfigureAwait(false));
        });
        return endpoints;
    }
}
