using Cave.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Maps the bounded machine-wide and workspace-scoped recent activity read endpoint.
/// </summary>
internal static class RecentActivityEndpoints
{
    private const int DefaultWindowMinutes = 15;
    private const int DefaultPageSize = 200;

    /// <summary>
    /// Adds the recent activity feed route to the HTTP host.
    /// </summary>
    /// <param name="endpoints">The host endpoint route builder.</param>
    /// <returns>The route builder for further endpoint mapping.</returns>
    public static IEndpointRouteBuilder MapRecentActivity(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/recent-activity",
            async Task<IResult> (
                int? minutes,
                string? workspace,
                int? offset,
                int? pageSize,
                WorkspaceRecentActivityService activity,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var feed = await activity.ReadAsync(
                        minutes ?? DefaultWindowMinutes,
                        workspace,
                        offset ?? 0,
                        pageSize ?? DefaultPageSize,
                        cancellationToken).ConfigureAwait(false);
                    if (feed.Coverage.Errors.Any(error => error.Code == "WorkspaceNotRegistered"))
                    {
                        return TypedResults.Problem(
                            statusCode: StatusCodes.Status404NotFound,
                            title: "The workspace is not registered.");
                    }

                    return TypedResults.Ok(feed);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    return TypedResults.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "The recent activity query is outside its supported bounds.",
                        detail: exception.Message);
                }
            });
        return endpoints;
    }
}
