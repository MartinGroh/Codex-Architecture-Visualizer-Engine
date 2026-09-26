using System.Xml.Linq;
using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>
/// Verifies that source documentation becomes concise, truthful architecture-node descriptions.
/// </summary>
public sealed class DocumentationSummaryExtractorTests
{
    /// <summary>
    /// Verifies that only the primary summary is retained and inline XML references remain readable.
    /// </summary>
    [Fact]
    public void ExtractsOnlySummaryAndRendersInlineReferences()
    {
        const string docstring = """
            <summary>
              Routes <see cref="T:Cave.Domain.ArchitectureNode"/> through <paramref name="projection"/>.
            </summary>
            <param name="projection">The projection that must not appear in the node summary.</param>
            <remarks>Implementation detail that must not appear in the node summary.</remarks>
            """;

        var summary = DocumentationSummaryExtractor.FromDocstring(docstring);

        Assert.Equal("Routes Cave.Domain.ArchitectureNode through projection.", summary);
    }

    /// <summary>
    /// Verifies that full summaries remain available to hover and detail views rather than being card-truncated.
    /// </summary>
    [Fact]
    public void PreservesTheCompleteSummary()
    {
        var expected = string.Join(" ", Enumerable.Repeat("architectural context", 24));

        var summary = DocumentationSummaryExtractor.FromDocstring($"<summary>{expected}</summary>");

        Assert.Equal(expected, summary);
        Assert.True(summary is { Length: > 240 });
    }

    /// <summary>
    /// Verifies that a declared MSBuild description overrides generic project-type copy.
    /// </summary>
    [Fact]
    public void ReadsProjectManifestDescription()
    {
        var manifest = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Description>  Coordinates semantic graph projections
                  for presentation clients. </Description>
              </PropertyGroup>
            </Project>
            """);

        var summary = DocumentationSummaryExtractor.FromProjectManifest(manifest);

        Assert.Equal("Coordinates semantic graph projections for presentation clients.", summary);
    }

    /// <summary>
    /// Verifies the bounded source fallback used for documented namespaces and provider gaps.
    /// </summary>
    [Fact]
    public void ReadsLeadingTripleSlashSummaryFromSource()
    {
        using var workspace = new TemporaryWorkspace();
        var sourcePath = Path.Combine(workspace.RootPath, "Feature.cs");
        File.WriteAllLines(sourcePath, [
            "/// <summary>",
            "/// Contains the checkout workflow and its <see cref=\"T:Product.Order\"/> model.",
            "/// </summary>",
            "namespace Product.Checkout;",
        ]);
        var reader = new CSharpDocumentationSummaryReader(workspace.RootPath);

        var summary = reader.Read("Feature.cs", declarationStartLine: 4);

        Assert.Equal("Contains the checkout workflow and its Product.Order model.", summary);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("cave-documentation-");

        internal string RootPath => root.FullName;

        public void Dispose() => root.Delete(recursive: true);
    }
}
