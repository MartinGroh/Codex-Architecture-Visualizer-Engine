namespace Cave.Domain;

/// <summary>Contains source-reported configured settings, not per-turn execution telemetry.</summary>
/// <param name="Model">The exact configured model identifier, or null when unavailable.</param>
/// <param name="ReasoningEffort">The configured effort identifier, or null when unset or unavailable.</param>
public sealed record AgentModelConfiguration(string? Model, string? ReasoningEffort);
