using Cave.Domain;
using System.Security.Cryptography;
using System.Text;

namespace Cave.Application;

/// <summary>Owns stable presentation names shared by browser snapshots and external Agent Flow clients.</summary>
public static class AgentDisplayNames
{
    /// <summary>Returns a stable opaque SHA256 key for external agent correlation.</summary>
    public static string PublicId(string agentId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agentId))).ToLowerInvariant();

    // CONSTRAINT: single names are intentional. The requested pool contains 48 female and 16 male
    // names. Names are presentation only; collisions never merge or replace authoritative IDs.
    private static readonly string[] Names =
    [
        "Ada", "Alice", "Amelia", "Anna", "Ava", "Bea", "Bella", "Clara",
        "Cleo", "Cora", "Daisy", "Dora", "Ella", "Ellie", "Emi", "Emma",
        "Eva", "Faye", "Flora", "Freya", "Grace", "Hana", "Hazel", "Iris",
        "Ivy", "Jade", "Jane", "Julia", "June", "Lea", "Lena", "Lily",
        "Luna", "Maya", "Mia", "Mila", "Mira", "Nora", "Olive", "Opal",
        "Rose", "Ruby", "Sara", "Sofia", "Tess", "Vera", "Lola", "Zoe",
        "Arlo", "Ben", "Eli", "Finn", "Hugo", "Jack", "Kai", "Leo",
        "Liam", "Luca", "Milo", "Nico", "Noah", "Oliver", "Owen", "Theo",
    ];

    /// <summary>Returns one stable first name for a subagent or the main-agent role label.</summary>
    /// <param name="agentId">The original observed identity, independent of ordering or work phase.</param>
    /// <param name="isSubagent">Whether this identity represents a child agent.</param>
    /// <returns>A deterministic, nonunique presentation name.</returns>
    public static string Get(string agentId, bool isSubagent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!isSubagent)
        {
            return "Main agent";
        }

        var hash = 2166136261u;
        foreach (var character in agentId)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        return Names[(hash ^ (hash >> 16)) % (uint)Names.Length];
    }

    /// <summary>Enriches an activity overlay without changing evidence, ordering, or stable identities.</summary>
    /// <param name="activity">The canonical observed and declared activity projection.</param>
    /// <returns>The same overlay with presentation names attached to every agent.</returns>
    public static AgentActivityOverlay Apply(AgentActivityOverlay activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity with
        {
            Agents = activity.Agents.Select(agent => agent with
            {
                DisplayName = Get(agent.AgentId, agent.IsSubagent),
                PublicId = PublicId(agent.AgentId),
            }).ToArray(),
        };
    }
}
