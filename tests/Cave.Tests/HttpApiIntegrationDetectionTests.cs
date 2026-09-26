using Cave.Domain;
using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>
/// Verifies cross-stack HTTP contract evidence without claiming a compile-time code reference.
/// </summary>
public sealed class HttpApiIntegrationDetectionTests
{
    /// <summary>
    /// Verifies that exact client/server route matches produce one inferred consumer-to-provider relation.
    /// </summary>
    [Fact]
    public void MatchesTypeScriptConsumersToAspNetCoreProviders()
    {
        using var workspace = new TemporaryWorkspace();
        var uiRoot = workspace.CreateDirectory("ui");
        var hostRoot = workspace.CreateDirectory("host");
        File.WriteAllText(
            Path.Combine(uiRoot, "api.ts"),
            "fetch('/api/snapshot'); new EventSource('/events');");
        File.WriteAllText(
            Path.Combine(hostRoot, "Program.cs"),
            "app.MapGet(\"/api/snapshot\", Handler); app.MapGet(\"/events\", Stream);");

        var relation = Assert.Single(CodeGraphSemanticIndex.MapHttpApiIntegrations([
            Project("ui", uiRoot, "project-type:typescript"),
            Project("host", hostRoot, "project-type:rest-api", "protocol:rest"),
        ]));

        Assert.Equal("ui", relation.SourceId);
        Assert.Equal("host", relation.TargetId);
        Assert.Equal(ArchitectureRelationKind.HttpApi, relation.Kind);
        Assert.Equal(EvidenceConfidence.Inferred, relation.Confidence);
        Assert.Equal(2, relation.EvidenceCount);
        Assert.Equal(4, relation.Weight);
    }

    /// <summary>
    /// Verifies that unrelated route literals and test-only clients do not create integration evidence.
    /// </summary>
    [Fact]
    public void RejectsUnmatchedAndTestOnlyRoutes()
    {
        using var workspace = new TemporaryWorkspace();
        var uiRoot = workspace.CreateDirectory("ui");
        var hostRoot = workspace.CreateDirectory("host");
        File.WriteAllText(Path.Combine(uiRoot, "api.ts"), "fetch('/api/client-only');");
        File.WriteAllText(Path.Combine(uiRoot, "api.test.ts"), "fetch('/api/server-only');");
        File.WriteAllText(Path.Combine(hostRoot, "Program.cs"), "app.MapGet(\"/api/server-only\", Handler);");

        var relations = CodeGraphSemanticIndex.MapHttpApiIntegrations([
            Project("ui", uiRoot, "project-type:typescript"),
            Project("host", hostRoot, "project-type:rest-api", "protocol:rest"),
        ]);

        Assert.Empty(relations);
    }

    private static HttpApiProject Project(string id, string rootPath, params string[] tags) =>
        new(id, rootPath, tags);

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("cave-http-api-");

        public string CreateDirectory(string name) => Directory.CreateDirectory(
            Path.Combine(root.FullName, name)).FullName;

        public void Dispose() => root.Delete(recursive: true);
    }
}
