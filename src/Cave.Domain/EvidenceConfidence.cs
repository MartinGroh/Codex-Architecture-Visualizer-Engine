namespace Cave.Domain;

/// <summary>
/// Describes how strongly the available evidence supports a graph fact.
/// </summary>
public enum EvidenceConfidence
{
    /// <summary>The fact comes from direct source or project structure.</summary>
    Exact,

    /// <summary>The fact was resolved from static semantic evidence.</summary>
    Inferred,

    /// <summary>The fact was derived from a named heuristic.</summary>
    Heuristic,

    /// <summary>The fact was explicitly declared by an agent.</summary>
    AgentDeclared,
}
