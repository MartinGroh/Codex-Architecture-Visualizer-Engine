using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Reads and writes the repository-local activity event stream used across CAVE processes.
/// </summary>
public interface IAgentActivityStore
{
    /// <summary>
    /// Reduces retained activity events into an overlay projected onto the supplied graph.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="graph">The current semantic graph.</param>
    /// <param name="cancellationToken">Signals that the read should stop.</param>
    /// <returns>The current activity overlay.</returns>
    Task<AgentActivityOverlay> ReadAsync(
        string workspaceRoot,
        ArchitectureGraph graph,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends an explicit agent scope declaration to the shared event stream.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="declaration">The validated scope declaration.</param>
    /// <param name="cancellationToken">Signals that the write should stop.</param>
    Task AppendScopeAsync(
        string workspaceRoot,
        AgentScopeDeclaration declaration,
        CancellationToken cancellationToken);
}

/// <summary>
/// Describes architecture scope explicitly declared by an agent.
/// </summary>
/// <param name="AgentId">The stable agent identifier chosen by the caller.</param>
/// <param name="AgentType">The agent role or type.</param>
/// <param name="IsSubagent">Whether this is a child agent.</param>
/// <param name="State">The declared lifecycle state.</param>
/// <param name="Summary">A concise description of the current work.</param>
/// <param name="Projects">Exact project ids, names, or qualified names.</param>
/// <param name="Namespaces">Exact namespace ids, names, or qualified names.</param>
/// <param name="Classes">Exact class, interface, or abstract-class ids, names, or qualified names.</param>
/// <param name="Files">Workspace-relative files in scope.</param>
public sealed record AgentScopeDeclaration(
    string AgentId,
    string AgentType,
    bool IsSubagent,
    AgentWorkState State,
    string? Summary,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> Files);
