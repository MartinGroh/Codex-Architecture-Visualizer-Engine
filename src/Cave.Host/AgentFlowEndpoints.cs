using Cave.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Maps the bounded, workspace-selected Agent Flow Light read endpoint.</summary>
internal static class AgentFlowEndpoints
{
    /// <summary>Adds the feed without acquiring semantic monitors or architecture projections.</summary>
    /// <param name="endpoints">The HTTP endpoint route builder.</param>
    /// <returns>The same builder for additional routes.</returns>
    public static IEndpointRouteBuilder MapAgentFlow(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/agent-flow", async Task<IResult> (
            string? workspace,
            HttpResponse response,
            WorkspaceCatalogService catalog,
            WorkspaceAgentFlowService flow,
            CancellationToken cancellationToken) =>
        {
            response.Headers.CacheControl = "no-store";
            if (string.IsNullOrWhiteSpace(workspace))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "A workspace identifier is required.");
            }

            var entry = await catalog.FindAsync(workspace, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "The workspace is not registered.");
            }

            if (!Directory.Exists(entry.WorkspaceRoot))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status410Gone,
                    title: "The workspace directory is unavailable.");
            }

            return TypedResults.Ok(await flow.ReadAsync(entry, cancellationToken).ConfigureAwait(false));
        });
        return endpoints;
    }
}
