using Cave.Infrastructure.Conversation;

/// <summary>
/// Ties the durable conversation bridge lifetime to the ASP.NET Core host.
/// </summary>
/// <param name="bridge">The canonical exact-task conversation bridge.</param>
internal sealed class ConversationBridgeHostedService(CodexConversationBridge bridge) : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => bridge.RunAsync(stoppingToken);
}
