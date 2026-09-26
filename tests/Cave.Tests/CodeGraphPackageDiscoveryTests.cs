using System.Xml.Linq;
using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>Verifies repository-boundary and direct NuGet package discovery.</summary>
public sealed class CodeGraphPackageDiscoveryTests
{
    /// <summary>A primary solution below the repository root keeps all of its projects and sources discoverable.</summary>
    [Fact]
    public void ProjectManifestDiscoveryIncludesProjectsUnderChildSolution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cave-project-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var solutionRoot = Path.Combine(root, "ConProbe");
            Directory.CreateDirectory(solutionRoot);
            File.WriteAllText(Path.Combine(solutionRoot, "ConProbe.slnx"), "<Solution />");

            var coreRoot = Path.Combine(solutionRoot, "ConProbe.Core");
            Directory.CreateDirectory(coreRoot);
            var coreProject = Path.Combine(coreRoot, "ConProbe.Core.csproj");
            File.WriteAllText(coreProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var hostRoot = Path.Combine(solutionRoot, "ConProbe.Host");
            Directory.CreateDirectory(hostRoot);
            var hostProject = Path.Combine(hostRoot, "ConProbe.Host.csproj");
            File.WriteAllText(hostProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var manifests = CodeGraphSemanticIndex.EnumerateProjectManifestPaths(root);
            var boundaries = CodeGraphSemanticIndex.EnumerateNestedRepositoryBoundaryPaths(root);

            Assert.Equal(2, manifests.Count);
            Assert.Contains(coreProject, manifests);
            Assert.Contains(hostProject, manifests);
            Assert.Empty(boundaries);
            Assert.False(CodeGraphSemanticIndex.IsSourceUnderNestedRepositoryBoundary(
                "ConProbe/ConProbe.Core/Services/ProbeService.cs",
                boundaries));
            Assert.False(CodeGraphSemanticIndex.IsSourceUnderNestedRepositoryBoundary(
                "ConProbe/ConProbe.Host/MainWindow.cs",
                boundaries));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Nested Git repositories and submodules remain outside the current product workspace.</summary>
    [Fact]
    public void ProjectManifestDiscoveryStopsAtNestedRepositoryBoundaries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cave-project-boundary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var rootProject = Path.Combine(root, "Root.csproj");
            File.WriteAllText(rootProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var nested = Path.Combine(root, "VendorSources");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, ".git"), "gitdir: ../.git/modules/VendorSources");
            File.WriteAllText(Path.Combine(nested, "Vendor.sln"), string.Empty);
            var nestedProject = Path.Combine(nested, "Vendor.csproj");
            File.WriteAllText(nestedProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var manifests = CodeGraphSemanticIndex.EnumerateProjectManifestPaths(root);
            var boundaries = CodeGraphSemanticIndex.EnumerateNestedRepositoryBoundaryPaths(root);

            Assert.Contains(rootProject, manifests);
            Assert.DoesNotContain(nestedProject, manifests);
            Assert.Contains("VendorSources", boundaries);
            Assert.True(CodeGraphSemanticIndex.IsSourceUnderNestedRepositoryBoundary(
                "VendorSources/Library/Thing.cs",
                boundaries));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Direct package versions can use either supported MSBuild form.</summary>
    [Fact]
    public void NuGetPackageReaderSupportsAttributeAndChildVersions()
    {
        var manifest = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Example.One" Version="1.2.3" />
                <PackageReference Include="Example.Two">
                  <Version>4.5.6</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        var packages = CodeGraphSemanticIndex.ReadNuGetPackageReferences(manifest);

        Assert.Equal(
            [
                new NuGetPackageReference("Example.One", "1.2.3"),
                new NuGetPackageReference("Example.Two", "4.5.6"),
            ],
            packages);
    }

    /// <summary>Unity's generated package cache is external while embedded packages remain editable.</summary>
    [Fact]
    public void UnityPackageCacheManifestsAreClassifiedAsExternalPackages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cave-unity-package-discovery-{Guid.NewGuid():N}");
        var cachedPackage = Path.Combine(
            root,
            "Library",
            "PackageCache",
            "com.unity.inputsystem@21a28c3a6c83",
            "package.json");
        var embeddedPackage = Path.Combine(root, "Packages", "com.example.editable", "package.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachedPackage)!);
            Directory.CreateDirectory(Path.GetDirectoryName(embeddedPackage)!);
            File.WriteAllText(cachedPackage, """{ "name": "com.unity.inputsystem" }""");
            File.WriteAllText(embeddedPackage, """{ "name": "com.example.editable" }""");

            var graph = CodeGraphSemanticIndex.MapGraph(root, new CodeGraphSemanticIndex.CodeGraphPayload());
            var cachedNode = Assert.Single(graph.Nodes, node => node.Name == "com.unity.inputsystem");
            var embeddedNode = Assert.Single(graph.Nodes, node => node.Name == "com.example.editable");

            Assert.Equal("external-package", cachedNode.CategoryId);
            Assert.Equal(
                ["unity", "project-type:unity-package", "package:unity", "source:external", "read-only"],
                cachedNode.Tags);
            Assert.Equal("project", embeddedNode.CategoryId);
            Assert.Equal(["typescript", "project-type:typescript"], embeddedNode.Tags);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
