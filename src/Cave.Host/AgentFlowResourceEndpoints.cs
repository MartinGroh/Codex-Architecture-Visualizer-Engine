using System.IO.Compression;
using System.Reflection;

/// <summary>Serves the exact portable resources embedded in this host build.</summary>
internal static class AgentFlowResourceEndpoints
{
    private const string Prefix = "AgentFlowPack/";
    private static readonly Assembly Resources = typeof(AgentFlowResourceEndpoints).Assembly;
    private static readonly Lazy<byte[]> Pack = new(CreatePack);

    /// <summary>Adds read-only schema and downloadable resource-pack endpoints.</summary>
    /// <param name="endpoints">The HTTP host route builder.</param>
    /// <returns>The same builder for further route mapping.</returns>
    public static IEndpointRouteBuilder MapAgentFlowResources(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/agent-flow/resources", () => TypedResults.File(
            Pack.Value, "application/zip", "cave-agent-flow-light-v1.zip"));
        endpoints.MapGet("/api/agent-flow/schema", () => TypedResults.Stream(
            Resources.GetManifestResourceStream(Prefix + "schema.json")
                ?? throw new InvalidOperationException("The Agent Flow schema is missing from this build."),
            "application/schema+json"));
        return endpoints;
    }

    private static byte[] CreatePack()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in Resources.GetManifestResourceNames()
                .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name[Prefix.Length..].Replace('\\', '/'), CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var source = Resources.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException("An embedded Agent Flow resource is missing.");
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }

        return buffer.ToArray();
    }
}
