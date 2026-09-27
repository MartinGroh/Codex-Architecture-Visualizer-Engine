using Cave.Application;
using Cave.Infrastructure.Activity;
using Cave.Infrastructure.CodeGraph;
using Cave.Infrastructure.Codex;
using Cave.Infrastructure.Conversation;
using Cave.Infrastructure.Git;
using Cave.Infrastructure.Live;
using Cave.Infrastructure.SemanticWorkspace;
using Cave.Infrastructure.Workspaces;
using Cave.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Apps;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(GitBaselineRequest.Upstream);
builder.Services.AddSingleton<IGitDeltaProvider, NativeGitDeltaProvider>();
builder.Services.AddSingleton<GitDeltaService>();
builder.Services.AddSingleton<IAgentActivityStore, FileAgentActivityStore>();
builder.Services.AddSingleton<IConversationStore, FileConversationStore>();
builder.Services.AddSingleton<ICodexGoalProvider>(services => new CodexAppServerGoalProvider(
    services.GetRequiredService<TimeProvider>(),
    builder.Configuration["Cave:CodexCommand"]));
builder.Services.AddSingleton<ICodexUsageProvider>(services => new CodexAppServerUsageProvider(
    services.GetRequiredService<TimeProvider>(),
    builder.Configuration["Cave:CodexCommand"]));
builder.Services.AddSingleton<CaveInfoService>();
builder.Services.AddSingleton<ISemanticIndex, CodeGraphSemanticIndex>();
builder.Services.AddSingleton<ArchitectureSnapshotService>();
builder.Services.AddSingleton<WorkspaceGraphMonitorFactory>();
builder.Services.AddSingleton<IWorkspaceCatalogStore>(services => new MachineWorkspaceCatalogStore(
    MachineWorkspaceCatalogStore.GetDefaultCatalogRoot(),
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<WorkspaceGraphMonitorRegistry>();
builder.Services.AddSingleton<ISemanticWorkspaceStore, FileSemanticWorkspaceStore>();
builder.Services.AddSingleton<SemanticWorkspaceService>();
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(1) });
builder.Services.AddSingleton<MachineViewerHostLauncher>();
builder.Services.AddHostedService<MachineViewerHostSupervisor>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CaveTools>()
    .WithTools<SemanticWorkspaceTools>()
    .WithResources<CaveResources>()
    .WithMcpApps();

await builder.Build().RunAsync();
