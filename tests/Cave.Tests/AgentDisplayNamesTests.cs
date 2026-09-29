using Cave.Application;
using Cave.Domain;

namespace Cave.Tests;

/// <summary>Verifies stable first-name presentation without changing observed identities or evidence.</summary>
public sealed class AgentDisplayNamesTests
{
    /// <summary>Verifies single names and the main role label.</summary>
    [Fact]
    public void NamesAreStableSingleNamesAndMainRoleRemainsExplicit()
    {
        Assert.Equal("Main agent", AgentDisplayNames.Get("main-task", false));
        Assert.Matches("^[A-Z][a-z]+$", AgentDisplayNames.Get("demo-reviewer", true));
        Assert.NotEqual(AgentDisplayNames.Get("demo-reviewer", true), AgentDisplayNames.Get("demo-docs", true));
    }

    /// <summary>Verifies all 64 requested names can be reached; repeated names do not imply shared identity.</summary>
    [Fact]
    public void RequestedPoolContainsSixtyFourNames()
    {
        var names = Enumerable.Range(0, 10000)
            .Select(index => AgentDisplayNames.Get($"child-{index}", true)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(64, names.Count);
        Assert.Contains("Ada", names);
        Assert.Contains("Theo", names);
        Assert.All(names, name => Assert.Matches("^[A-Z][a-z]+$", name));
    }

    /// <summary>Verifies enrichment adds only presentation identity while preserving observed evidence.</summary>
    [Fact]
    public void EnrichmentChangesOnlyPresentationName()
    {
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentActivity("child", "default", true, AgentWorkState.Active,
            AgentActivityPhase.Validating, "Check the API", true, true, now, now)
        {
            SummaryEvidence = AgentActivityEvidenceKind.Declared,
        };
        var original = new AgentActivityOverlay([agent], [], [], null, [], null)
        {
            SourceStatus = AgentActivitySourceStatus.Ready,
        };
        var result = AgentDisplayNames.Apply(original);
        Assert.Equal(original.SourceStatus, result.SourceStatus);
        var enriched = Assert.Single(result.Agents);
        Assert.Equal(agent, enriched with { DisplayName = null, PublicId = null });
        Assert.Equal(AgentDisplayNames.Get("child", true), enriched.DisplayName);
        Assert.Equal(AgentDisplayNames.PublicId("child"), enriched.PublicId);
        Assert.Null(agent.DisplayName);
    }
}
