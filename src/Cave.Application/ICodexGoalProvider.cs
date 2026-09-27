using Cave.Domain;

namespace Cave.Application;

/// <summary>Reads the native goal of an already identified Codex task without changing task ownership.</summary>
public interface ICodexGoalProvider
{
    /// <summary>Reads only the supplied exact task; never enumerates or selects another task.</summary>
    /// <param name="sessionId">The task identifier observed by the workspace conversation control owner.</param>
    /// <param name="cancellationToken">Signals that the bounded read should stop.</param>
    /// <returns>A native goal, explicit absence, or an unavailable result for that same task.</returns>
    Task<CodexGoalSnapshot> GetAsync(string sessionId, CancellationToken cancellationToken);
}
