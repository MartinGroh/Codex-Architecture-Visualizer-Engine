using System.Xml.Linq;
using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>
/// Verifies deterministic project-type classification from MSBuild manifest evidence.
/// </summary>
public sealed class DotNetProjectClassifierTests
{
    /// <summary>
    /// Verifies the supported project families and their canonical visualization tags.
    /// </summary>
    /// <param name="projectXml">The representative project manifest.</param>
    /// <param name="projectName">The project name used for the bounded test-name convention.</param>
    /// <param name="expectedType">The expected project family.</param>
    /// <param name="expectedTag">The expected normalized type tag.</param>
    [Theory]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk'><ItemGroup><PackageReference Include='Microsoft.NET.Test.Sdk' /></ItemGroup></Project>", "Product.Specs", "Test", "project-type:test")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk'></Project>", "Product.Core", "ClassLibrary", "project-type:class-library")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk.Web'></Project>", "Product.Api", "RestApi", "project-type:rest-api")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk.Web'><ItemGroup><PackageReference Include='Grpc.AspNetCore' /></ItemGroup></Project>", "Product.Grpc", "GrpcApi", "project-type:grpc-api")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>", "Product.Cli", "Executable", "project-type:executable")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup></Project>", "Product.Forms", "WinForms", "project-type:winforms")]
    [InlineData("<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><UseWinUI>true</UseWinUI></PropertyGroup></Project>", "Product.Desktop", "WinUi", "project-type:winui")]
    public void ClassifyRecognizesSupportedProjectTypes(
        string projectXml,
        string projectName,
        string expectedType,
        string expectedTag)
    {
        var classification = DotNetProjectClassifier.Classify(XDocument.Parse(projectXml), projectName);

        Assert.Equal(expectedType, classification.Type.ToString());
        Assert.Equal(expectedTag, classification.TypeTag);
    }

    /// <summary>
    /// Verifies that a gRPC web host keeps both protocol capabilities while using the gRPC primary type.
    /// </summary>
    [Fact]
    public void ClassifyPreservesGrpcAndRestCapabilities()
    {
        var manifest = XDocument.Parse(
            "<Project Sdk='Microsoft.NET.Sdk.Web'><ItemGroup><Protobuf Include='service.proto' GrpcServices='Server' /></ItemGroup></Project>");

        var classification = DotNetProjectClassifier.Classify(manifest, "Product.Service");

        Assert.Equal(DotNetProjectType.GrpcApi, classification.Type);
        Assert.Equal(["protocol:rest", "protocol:grpc"], classification.CapabilityTags);
    }
}
