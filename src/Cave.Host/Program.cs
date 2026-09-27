using System.Text.Json;
using System.Text.Json.Serialization;
using Cave.Application;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.CodeGraph;
using Cave.Infrastructure.Codex;
using Cave.Infrastructure.Conversation;
using Cave.Infrastructure.Demo;
using Cave.Infrastructure.Git;
using Cave.Infrastructure.Live;
using Cave.Infrastructure.Sample;
using Cave.Infrastructure.SemanticWorkspace;
using Cave.Infrastructure.Workspaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    var semanticOptions = SemanticWorkspaceJson.CreateOptions();
    foreach (var converter in semanticOptions.Converters)
    {
        options.SerializerOptions.Converters.Add(converter);
    }
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(GitBaselineRequest.Upstream);

var demoMode = builder.Configuration.GetValue("Cave:DemoMode", false);
if (demoMode)
{
    builder.Services.AddSingleton<ISemanticIndex, DemoSemanticIndex>();
    builder.Services.AddSingleton<IGitDeltaProvider, DemoGitDeltaProvider>();
    builder.Services.AddSingleton<IAgentActivityStore, DemoAgentActivityStore>();
    builder.Services.AddSingleton<IAgentActivityHistoryStore>(services =>
        (IAgentActivityHistoryStore)services.GetRequiredService<IAgentActivityStore>());
    builder.Services.AddSingleton<IConversationStore, DemoConversationStore>();
    builder.Services.AddSingleton<IConversationControl, DemoConversationControl>();
    builder.Services.AddSingleton<ICodexUsageProvider, DemoCodexUsageProvider>();
    builder.Services.AddSingleton<ICodexGoalProvider, DemoCodexGoalProvider>();
}
else
{
    builder.Services.AddSingleton<IGitDeltaProvider, NativeGitDeltaProvider>();
    builder.Services.AddSingleton<FileAgentActivityStore>();
    builder.Services.AddSingleton<IAgentActivityStore>(services =>
        services.GetRequiredService<FileAgentActivityStore>());
    builder.Services.AddSingleton<IAgentActivityHistoryStore>(services =>
        services.GetRequiredService<FileAgentActivityStore>());
    builder.Services.AddSingleton<ICodexTaskLocator>(services =>
        services.GetRequiredService<FileAgentActivityStore>());
    builder.Services.AddSingleton<IConversationStore, FileConversationStore>();
    builder.Services.AddSingleton<ICodexGoalProvider>(services => new CodexAppServerGoalProvider(
        services.GetRequiredService<TimeProvider>(),
        builder.Configuration["Cave:CodexCommand"]));
    builder.Services.AddSingleton<ICodexUsageProvider>(services => new CodexAppServerUsageProvider(
        services.GetRequiredService<TimeProvider>(),
        builder.Configuration["Cave:CodexCommand"]));
    builder.Services.AddSingleton<ICodexTurnRunner>(services => new CodexAppServerTurnRunner(
        services.GetRequiredService<IConversationStore>(),
        services.GetRequiredService<TimeProvider>(),
        builder.Configuration["Cave:CodexCommand"]));
    builder.Services.AddSingleton<CodexConversationBridge>();
    builder.Services.AddSingleton<IConversationControl>(services =>
        services.GetRequiredService<CodexConversationBridge>());
    builder.Services.AddHostedService<ConversationBridgeHostedService>();

    var providerMode = builder.Configuration["Cave:SemanticProvider"] ?? "CodeGraph";
    if (providerMode.Equals("CodeGraph", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<ISemanticIndex, CodeGraphSemanticIndex>();
    }
    else if (providerMode.Equals("Sample", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<ISemanticIndex, SampleSemanticIndex>();
    }
    else
    {
        throw new InvalidOperationException(
            $"Unsupported CAVE semantic provider '{providerMode}'. Choose 'CodeGraph' or 'Sample'.");
    }
}

builder.Services.AddSingleton<GitDeltaService>();
builder.Services.AddSingleton<CaveInfoService>();
builder.Services.AddSingleton<ArchitectureSnapshotService>();
builder.Services.AddSingleton<WorkspaceGraphMonitorFactory>();
builder.Services.AddSingleton<IWorkspaceCatalogStore>(services => new MachineWorkspaceCatalogStore(
    builder.Configuration["Cave:WorkspaceCatalogRoot"]
        ?? MachineWorkspaceCatalogStore.GetDefaultCatalogRoot(),
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<WorkspaceCatalogService>();
builder.Services.AddSingleton<WorkspaceActivityService>();
builder.Services.AddSingleton<WorkspaceRecentActivityService>();
builder.Services.AddSingleton<WorkspaceGraphMonitorRegistry>();
builder.Services.AddSingleton<ISemanticWorkspaceStore, FileSemanticWorkspaceStore>();
builder.Services.AddSingleton<SemanticWorkspaceService>();

var app = builder.Build();
var browserSemanticActor = new Cave.Domain.SemanticActor(
    Cave.Domain.SemanticActorType.Human,
    "cave-browser",
    "CAVE browser user");
var catalogStore = app.Services.GetRequiredService<IWorkspaceCatalogStore>();
var configuredRoot = builder.Configuration["Cave:WorkspaceRoot"];
if (!string.IsNullOrWhiteSpace(configuredRoot))
{
    await catalogStore.RegisterAsync(configuredRoot, CancellationToken.None);
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapGet(
    "/api/runtime",
    () => TypedResults.Ok(new CaveRuntimeInfo(CaveRuntimeContract.ViewerProtocolVersion)));
app.MapGet(
    "/api/info",
    async (CaveInfoService info, CancellationToken cancellationToken) =>
        TypedResults.Ok(await info.GetAsync(cancellationToken)));
app.MapGet(
    "/api/workspaces",
    async (WorkspaceCatalogService catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.GetOverviewAsync(cancellationToken)));
app.MapRecentActivity();
app.MapGet(
    "/api/activity",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceActivityService activity,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return Results.Problem(statusCode: 400, title: "A workspace identifier is required.");
        }

        var entry = await catalog.FindAsync(workspace, cancellationToken);
        if (entry is null)
        {
            return Results.Problem(statusCode: 404, title: "The workspace is not registered.");
        }

        if (!Directory.Exists(entry.WorkspaceRoot))
        {
            return Results.Problem(statusCode: 410, title: "The workspace directory is unavailable.");
        }

        return Results.Ok(await activity.ReadAsync(entry, cancellationToken));
    });

app.MapPost(
    "/api/conversation/sharing",
    async (
        string? workspace,
        ConversationSharingRequest request,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        IConversationStore conversations,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        await conversations.SetSharingAsync(
            resolution.Session!.Monitor.WorkspaceRoot,
            request.Enabled,
            cancellationToken);
        return Results.NoContent();
    });
app.MapPost(
    "/api/conversation/messages",
    async (
        string? workspace,
        ConversationSendRequest request,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        IConversationControl conversationControl,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        try
        {
            var delivery = await conversationControl.QueueAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                request.ExpectedSessionId,
                request.Text,
                cancellationToken);
            return Results.Accepted(value: delivery);
        }
        catch (CodexTaskConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Codex task binding changed.",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The conversation message is invalid.",
                detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The conversation bridge is not ready.",
                detail: exception.Message);
        }
    });
app.MapPost(
    "/api/conversation/node-memos",
    async (
        string? workspace,
        NodeMemoRequest request,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        IConversationControl conversationControl,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        try
        {
            var text = await conversationControl.RunNodeMemoAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                request.ExpectedSessionId,
                request.Text,
                cancellationToken);
            return Results.Ok(new NodeMemoResponse(text));
        }
        catch (CodexTaskConflictException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The Codex task binding changed.",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The node question is invalid.",
                detail: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "The temporary Codex side chat failed.",
                detail: exception.Message);
        }
    });
app.MapGet(
    "/api/snapshot",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        return resolution.Error
            ?? TypedResults.Ok(await resolution.Session!.Monitor.GetCurrentAsync(cancellationToken));
    });

app.MapGet(
    "/api/semantic-workspace",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        return resolution.Error
            ?? TypedResults.Ok(await semanticWorkspaces.GetAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                cancellationToken));
    });

app.MapPost(
    "/api/semantic-workspace/operations",
    async (
        string? workspace,
        SemanticWorkspaceBatch batch,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        return resolution.Error
            ?? TypedResults.Ok(await semanticWorkspaces.ApplyOperationsAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                batch,
                cancellationToken));
    });

app.MapPost(
    "/api/semantic-workspace/undo",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        return resolution.Error
            ?? TypedResults.Ok(await semanticWorkspaces.UndoAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                browserSemanticActor,
                cancellationToken));
    });

app.MapPost(
    "/api/semantic-workspace/redo",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        return resolution.Error
            ?? TypedResults.Ok(await semanticWorkspaces.RedoAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                browserSemanticActor,
                cancellationToken));
    });

app.MapGet(
    "/api/semantic-workspace/summary",
    async (
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var semanticWorkspace = await semanticWorkspaces.GetAsync(
            resolution.Session!.Monitor.WorkspaceRoot,
            cancellationToken);
        return TypedResults.Ok(SemanticWorkspaceReadModelService.GetWorkspaceSummary(semanticWorkspace));
    });

app.MapGet(
    "/api/semantic-workspace/diagrams/{diagramId}",
    async (
        string diagramId,
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var semanticWorkspace = await semanticWorkspaces.GetAsync(
            resolution.Session!.Monitor.WorkspaceRoot,
            cancellationToken);
        var diagram = SemanticWorkspaceReadModelService.FindDiagram(semanticWorkspace, diagramId);
        return diagram is null
            ? Results.NotFound()
            : TypedResults.Ok(diagram);
    });

app.MapGet(
    "/api/semantic-workspace/entities/{entityId}/context",
    async (
        string entityId,
        string? workspace,
        int? depth,
        int? maximumEntities,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var acceptedDepth = depth ?? 1;
        var acceptedMaximum = maximumEntities
            ?? SemanticWorkspaceReadModelService.DefaultMaximumEntities;
        try
        {
            var semanticWorkspace = await semanticWorkspaces.GetAsync(
                resolution.Session!.Monitor.WorkspaceRoot,
                cancellationToken);
            var context = SemanticWorkspaceReadModelService.FindEntityContext(
                semanticWorkspace,
                entityId,
                acceptedDepth,
                acceptedMaximum);
            return context is null
                ? Results.NotFound()
                : TypedResults.Ok(context);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The semantic context bounds are invalid.",
                detail: exception.Message);
        }
    });

app.MapGet(
    "/api/semantic-workspace/export",
    async (
        string? workspace,
        string? format,
        bool? includePresentation,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        SemanticWorkspaceService semanticWorkspaces,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        var semanticWorkspace = await semanticWorkspaces.GetAsync(
            resolution.Session!.Monitor.WorkspaceRoot,
            cancellationToken);
        var acceptedIncludePresentation = includePresentation ?? false;
        if (string.IsNullOrWhiteSpace(format)
            || format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var json = SemanticWorkspaceExportService.ExportJson(
                semanticWorkspace,
                new SemanticWorkspaceExportOptions(acceptedIncludePresentation, WriteIndented: true));
            return TypedResults.Ok(new SemanticWorkspaceExportResponse(json));
        }

        if (format.Equals("markdown", StringComparison.OrdinalIgnoreCase)
            || format.Equals("md", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Ok(new SemanticWorkspaceExportResponse(
                SemanticWorkspaceExportService.ExportHumanReadable(semanticWorkspace)));
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The semantic export format is unsupported.",
            detail: "Choose 'json' or 'markdown'.");
    });

app.MapGet(
    "/events",
    async (
        HttpContext context,
        string? workspace,
        WorkspaceCatalogService catalog,
        WorkspaceGraphMonitorRegistry registry,
        CancellationToken cancellationToken) =>
    {
        var resolution = await ResolveWorkspaceAsync(workspace, catalog, registry, cancellationToken);
        if (resolution.Error is not null)
        {
            return resolution.Error;
        }

        await StreamArchitectureEventsAsync(context, resolution.Session!.Monitor, cancellationToken);
        return Results.Empty;
    });

app.MapFallbackToFile("index.html");

app.Run();

static async Task<WorkspaceResolution> ResolveWorkspaceAsync(
    string? workspaceId,
    WorkspaceCatalogService catalog,
    WorkspaceGraphMonitorRegistry registry,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(workspaceId))
    {
        return new WorkspaceResolution(
            null,
            Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "A workspace selection is required.",
                detail: "Open a project from the CAVE workspace dashboard."));
    }

    var entry = await catalog.FindAsync(workspaceId, cancellationToken).ConfigureAwait(false);
    if (entry is null)
    {
        return new WorkspaceResolution(
            null,
            Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "The workspace is not registered.",
                detail: "Return to the CAVE workspace dashboard and choose an available project."));
    }

    if (!Directory.Exists(entry.WorkspaceRoot))
    {
        return new WorkspaceResolution(
            null,
            Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "The workspace is unavailable.",
                detail: "The registered project directory no longer exists on this machine."));
    }

    return new WorkspaceResolution(
        await registry.GetOrCreateAsync(entry.WorkspaceRoot, cancellationToken).ConfigureAwait(false),
        null);
}

static async Task StreamArchitectureEventsAsync(
    HttpContext context,
    WorkspaceGraphMonitor monitor,
    CancellationToken cancellationToken)
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Append("X-Accel-Buffering", "no");

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    jsonOptions.Converters.Add(new JsonStringEnumConverter());
    var afterVersion = 0L;

    while (!cancellationToken.IsCancellationRequested)
    {
        var update = await monitor.WaitForUpdateAsync(
            afterVersion,
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (update.Version == afterVersion)
        {
            await context.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
        }
        else
        {
            var payload = JsonSerializer.Serialize(update, jsonOptions);
            await context.Response.WriteAsync($"event: snapshot\ndata: {payload}\n\n", cancellationToken);
            afterVersion = update.Version;
        }

        await context.Response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// Provides the host entry-point marker used by integration tests.
/// </summary>
public partial class Program;

internal sealed record WorkspaceResolution(
    WorkspaceGraphSession? Session,
    IResult? Error);

internal sealed record ConversationSharingRequest(bool Enabled);

internal sealed record ConversationSendRequest(string ExpectedSessionId, string Text);

internal sealed record NodeMemoRequest(string ExpectedSessionId, string Text);

internal sealed record NodeMemoResponse(string Text);

internal sealed record SemanticWorkspaceExportResponse(string Text);
