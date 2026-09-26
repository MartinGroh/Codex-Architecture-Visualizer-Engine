using Cave.Domain;
using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>Verifies that provider declaration nodes become stable logical C# types.</summary>
public sealed class CodeGraphPartialTypeNormalizationTests
{
    /// <summary>Partial declarations retain every source span and all external relation evidence.</summary>
    [Fact]
    public void PartialTypeDeclarationsMergeWithinTheirOwningProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cave-partial-types-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(root, "Example");
        Directory.CreateDirectory(projectRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(projectRoot, "Example.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(projectRoot, "PartialThing.cs"), "namespace Example; partial class PartialThing { }");
            File.WriteAllText(Path.Combine(projectRoot, "PartialThing.More.cs"), "namespace Example; partial class PartialThing { }");
            File.WriteAllText(Path.Combine(projectRoot, "Dependency.cs"), "namespace Example; class Dependency { }");

            var payload = new CodeGraphSemanticIndex.CodeGraphPayload
            {
                Nodes =
                [
                    Node("namespace:example", "namespace", "Example", "Example", "Example/PartialThing.cs", 1, 1),
                    Node("class:partial-1", "class", "PartialThing", "Example::PartialThing", "Example/PartialThing.cs", 1, 1),
                    Node("class:partial-2", "class", "PartialThing", "Example::PartialThing", "Example/PartialThing.More.cs", 1, 1),
                    Node("class:dependency", "class", "Dependency", "Example::Dependency", "Example/Dependency.cs", 1, 1),
                ],
                Edges =
                [
                    Edge("namespace:example", "class:partial-1", "contains"),
                    Edge("namespace:example", "class:partial-2", "contains"),
                    Edge("namespace:example", "class:dependency", "contains"),
                    Edge("class:partial-1", "class:dependency", "references"),
                    Edge("class:partial-2", "class:dependency", "references"),
                ],
            };

            var graph = CodeGraphSemanticIndex.MapGraph(root, payload);

            var partial = Assert.Single(graph.Nodes, node => node.QualifiedName == "Example::PartialThing");
            Assert.Equal(ArchitectureNodeKind.Class, partial.Kind);
            Assert.Equal(2, partial.SourceLocations.Count);
            Assert.Contains(partial.SourceLocations, location => location.FilePath == "Example/PartialThing.cs");
            Assert.Contains(partial.SourceLocations, location => location.FilePath == "Example/PartialThing.More.cs");

            var dependency = Assert.Single(graph.Nodes, node => node.QualifiedName == "Example::Dependency");
            var relation = Assert.Single(graph.Relations, item =>
                item.SourceId == partial.Id && item.TargetId == dependency.Id);
            Assert.Equal(ArchitectureRelationKind.DependsOn, relation.Kind);
            Assert.Equal(2, relation.EvidenceCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CodeGraphSemanticIndex.CodeGraphNode Node(
        string id,
        string kind,
        string name,
        string qualifiedName,
        string filePath,
        int startLine,
        int endLine) =>
        new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            QualifiedName = qualifiedName,
            FilePath = filePath,
            Language = "csharp",
            StartLine = startLine,
            EndLine = endLine,
        };

    private static CodeGraphSemanticIndex.CodeGraphEdge Edge(string source, string target, string kind) =>
        new()
        {
            Source = source,
            Target = target,
            Kind = kind,
        };
}
