using Cave.Application;

/// <summary>Maps exact-task published conversation and shared raster reads.</summary>
internal static class AgentChatEndpoints
{
    /// <summary>Registers the scoped, non-cacheable agent chat endpoints.</summary>
    public static IEndpointRouteBuilder MapAgentChat(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/agent-chat");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";
            return await next(context);
        });
        group.MapGet("", async Task<IResult> (
            string? workspace, string? task, WorkspaceCatalogService catalog,
            WorkspaceAgentChatService chats, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(workspace) || !ValidOpaqueId(task))
                return TypedResults.Problem(statusCode: 400, title: "A workspace and task identifier are required.");
            var entry = await catalog.FindAsync(workspace, cancellationToken);
            if (entry is null || !Directory.Exists(entry.WorkspaceRoot))
                return TypedResults.NotFound();
            var result = await chats.ReadAsync(entry, task!, cancellationToken);
            return result is not null ? TypedResults.Ok(result) : TypedResults.NotFound();
        });
        group.MapGet("/artifacts", async Task<IResult> (
            string? workspace, string? task, string? artifact, WorkspaceCatalogService catalog,
            WorkspaceAgentChatService chats, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(workspace) || !ValidOpaqueId(task) || !ValidOpaqueId(artifact))
                return TypedResults.Problem(statusCode: 400, title: "Workspace, task, and artifact identifiers are required.");
            var entry = await catalog.FindAsync(workspace, cancellationToken);
            if (entry is null || !Directory.Exists(entry.WorkspaceRoot))
                return TypedResults.NotFound();
            var image = await chats.ReadArtifactAsync(entry, task!, artifact!, cancellationToken);
            return image is not null ? TypedResults.Bytes(image.Bytes, image.ContentType) : TypedResults.NotFound();
        });
        return endpoints;
    }

    private static bool ValidOpaqueId(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
