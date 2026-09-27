namespace Cave.Domain;

/// <summary>Describes whether the exact task's native goal could be read.</summary>
public enum CodexGoalSourceStatus
{
    /// <summary>The native endpoint replied, possibly without a goal.</summary>
    Ready,

    /// <summary>The exact goal could not be read; no objective is exposed.</summary>
    Unavailable,
}

/// <summary>Preserves the native Codex goal lifecycle independently from agent activity.</summary>
public enum CodexGoalStatus
{
    /// <summary>The goal is active.</summary>
    Active,
    /// <summary>The owner paused the goal.</summary>
    Paused,
    /// <summary>The goal is blocked.</summary>
    Blocked,
    /// <summary>Account usage limits suspended goal work.</summary>
    UsageLimited,
    /// <summary>The goal's token budget suspended work.</summary>
    BudgetLimited,
    /// <summary>The goal is complete.</summary>
    Complete,
}

/// <summary>Contains only the native goal fields for one exactly identified Codex task.</summary>
/// <param name="Objective">The user-visible native goal objective.</param>
/// <param name="Status">The native goal lifecycle.</param>
/// <param name="TokenBudget">The optional native token budget; null means no budget was supplied.</param>
/// <param name="TokensUsed">The native goal token count, including a valid zero.</param>
/// <param name="TimeUsedSeconds">The native elapsed goal time in seconds.</param>
/// <param name="CreatedAt">The native creation timestamp, preserved without guessing its units.</param>
/// <param name="UpdatedAt">The native update timestamp, preserved without guessing its units.</param>
public sealed record CodexGoal(
    string Objective,
    CodexGoalStatus Status,
    long? TokenBudget,
    long TokensUsed,
    long TimeUsedSeconds,
    long CreatedAt,
    long UpdatedAt);

/// <summary>Represents a read-only native goal lookup for an exact hook-observed task.</summary>
/// <param name="Status">Whether the endpoint replied successfully.</param>
/// <param name="SessionId">The exact hooked task identifier used for the lookup.</param>
/// <param name="Goal">The native goal, or null for an absent or unavailable goal.</param>
/// <param name="RetrievedAtUtc">When CAVE performed this read.</param>
/// <param name="Error">A bounded diagnostic when unavailable.</param>
public sealed record CodexGoalSnapshot(
    CodexGoalSourceStatus Status,
    string SessionId,
    CodexGoal? Goal,
    DateTimeOffset RetrievedAtUtc,
    string? Error);
