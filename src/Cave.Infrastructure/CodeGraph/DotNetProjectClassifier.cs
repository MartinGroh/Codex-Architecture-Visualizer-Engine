using System.Xml.Linq;

namespace Cave.Infrastructure.CodeGraph;

internal static class DotNetProjectClassifier
{
    private static readonly HashSet<string> TestPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "NUnit",
        "MSTest.TestFramework",
    };

    internal static DotNetProjectClassification Classify(XDocument document, string projectName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var packageReferences = AttributeValues(document, "PackageReference", "Include");
        var frameworkReferences = AttributeValues(document, "FrameworkReference", "Include");
        var isWeb = ProjectSdks(document).Contains("Microsoft.NET.Sdk.Web", StringComparer.OrdinalIgnoreCase)
            || frameworkReferences.Contains("Microsoft.AspNetCore.App", StringComparer.OrdinalIgnoreCase);
        var isGrpc = packageReferences.Contains("Grpc.AspNetCore", StringComparer.OrdinalIgnoreCase)
            || document.Descendants().Any(element =>
                element.Name.LocalName.Equals("Protobuf", StringComparison.OrdinalIgnoreCase)
                && IsServerGrpc(element.Attribute("GrpcServices")?.Value));

        var type = IsTrueProperty(document, "IsTestProject")
                || packageReferences.Any(TestPackages.Contains)
                || HasTestProjectName(projectName)
            ? DotNetProjectType.Test
            : IsTrueProperty(document, "UseWinUI")
                || packageReferences.Contains("Microsoft.WindowsAppSDK", StringComparer.OrdinalIgnoreCase)
                ? DotNetProjectType.WinUi
                : IsTrueProperty(document, "UseWindowsForms")
                    ? DotNetProjectType.WinForms
                    : isGrpc
                        ? DotNetProjectType.GrpcApi
                        : isWeb
                            ? DotNetProjectType.RestApi
                            : IsExecutable(document)
                                ? DotNetProjectType.Executable
                                : DotNetProjectType.ClassLibrary;

        var capabilityTags = new List<string>();
        if (isWeb)
        {
            capabilityTags.Add("protocol:rest");
        }

        if (isGrpc)
        {
            capabilityTags.Add("protocol:grpc");
        }

        return new DotNetProjectClassification(type, capabilityTags);
    }

    private static bool IsExecutable(XDocument document)
    {
        var outputType = PropertyValue(document, "OutputType");
        return outputType is not null
            && (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTrueProperty(XDocument document, string propertyName) =>
        bool.TryParse(PropertyValue(document, propertyName), out var value) && value;

    private static string? PropertyValue(XDocument document, string propertyName) =>
        document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

    private static string[] AttributeValues(
        XDocument document,
        string elementName,
        string attributeName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
                ?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

    private static List<string> ProjectSdks(XDocument document)
    {
        var sdks = new List<string>();
        var rootSdk = document.Root?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(rootSdk))
        {
            sdks.AddRange(rootSdk.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        sdks.AddRange(AttributeValues(document, "Sdk", "Name"));
        return sdks;
    }

    private static bool IsServerGrpc(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("Server", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Both", StringComparison.OrdinalIgnoreCase);

    private static bool HasTestProjectName(string projectName) =>
        projectName.Equals("Tests", StringComparison.OrdinalIgnoreCase)
        || projectName.Equals("Test", StringComparison.OrdinalIgnoreCase)
        || projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
        || projectName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase);
}

internal sealed record DotNetProjectClassification(
    DotNetProjectType Type,
    IReadOnlyList<string> CapabilityTags)
{
    internal string TypeTag => Type switch
    {
        DotNetProjectType.Test => "project-type:test",
        DotNetProjectType.ClassLibrary => "project-type:class-library",
        DotNetProjectType.RestApi => "project-type:rest-api",
        DotNetProjectType.GrpcApi => "project-type:grpc-api",
        DotNetProjectType.Executable => "project-type:executable",
        DotNetProjectType.WinForms => "project-type:winforms",
        DotNetProjectType.WinUi => "project-type:winui",
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null),
    };

    internal string Description => Type switch
    {
        DotNetProjectType.Test => ".NET test project",
        DotNetProjectType.ClassLibrary => ".NET class library",
        DotNetProjectType.RestApi => "ASP.NET Core REST API",
        DotNetProjectType.GrpcApi => "ASP.NET Core gRPC API",
        DotNetProjectType.Executable => ".NET CLI / executable",
        DotNetProjectType.WinForms => "Windows Forms desktop app",
        DotNetProjectType.WinUi => "WinUI desktop app",
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null),
    };
}

internal enum DotNetProjectType
{
    Test,
    ClassLibrary,
    RestApi,
    GrpcApi,
    Executable,
    WinForms,
    WinUi,
}
