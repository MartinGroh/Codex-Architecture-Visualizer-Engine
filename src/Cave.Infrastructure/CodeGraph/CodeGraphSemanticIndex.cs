using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.CodeGraph;

/// <summary>
/// Adapts the installed CodeGraph public SDK into CAVE's normalized architecture graph.
/// </summary>
public sealed class CodeGraphSemanticIndex : ISemanticIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex HttpApiProviderRoutePattern = new(
        @"\.Map(?:Get|Post|Put|Delete|Patch|Methods)\s*\(\s*""(?<route>/[^""?#]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HttpApiConsumerRoutePattern = new(
        @"\b(?:fetch|EventSource)\s*\(\s*['""`](?<route>/[^'""`?#]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public async Task<SemanticIndexResult> ReadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{canonicalRoot}' does not exist.");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var nodeExecutable = Path.Combine(localAppData, "codegraph", "current", "node.exe");
        var codeGraphLibrary = Path.Combine(localAppData, "codegraph", "current", "lib", "dist", "index.js");
        var helperScript = Path.Combine(AppContext.BaseDirectory, "CodeGraph", "codegraph-snapshot.mjs");

        foreach (var requiredPath in new[] { nodeExecutable, codeGraphLibrary, helperScript })
        {
            if (!File.Exists(requiredPath))
            {
                throw new FileNotFoundException(
                    $"The live CodeGraph provider requires '{requiredPath}'.",
                    requiredPath);
            }
        }

        var startInfo = CreateSnapshotStartInfo(nodeExecutable, helperScript, canonicalRoot);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The CodeGraph adapter process could not be started.");
        }

        // CONSTRAINT: the provider is also hosted inside the MCP stdio process. Its child must receive
        // a closed private stdin rather than inheriting the JSON-RPC transport owned by the MCP server.
        process.StandardInput.Close();

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"CodeGraph failed for '{canonicalRoot}' with exit code {process.ExitCode}: {error.Trim()}");
        }

        var payload = JsonSerializer.Deserialize<CodeGraphPayload>(output, JsonOptions)
            ?? throw new InvalidOperationException("CodeGraph returned an empty semantic payload.");

        return new SemanticIndexResult(
            MapGraph(canonicalRoot, payload),
            new DirectoryInfo(canonicalRoot).Name,
            "codegraph-1.5.0",
            SnapshotSourceKind.CodeGraph,
            IsLive: true);
    }

    internal static ProcessStartInfo CreateSnapshotStartInfo(
        string nodeExecutable,
        string helperScript,
        string canonicalRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            WorkingDirectory = canonicalRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(helperScript);
        startInfo.ArgumentList.Add(canonicalRoot);
        return startInfo;
    }

    internal static ArchitectureGraph MapGraph(string workspaceRoot, CodeGraphPayload payload)
    {
        var projects = DiscoverProjects(workspaceRoot);
        var packages = DiscoverNuGetPackages(projects);
        var nestedRepositoryBoundaries = EnumerateNestedRepositoryBoundaryPaths(workspaceRoot);
        var sourceDocumentation = new CSharpDocumentationSummaryReader(workspaceRoot);
        var nodes = new Dictionary<string, ArchitectureNode>(StringComparer.Ordinal);
        const string groupId = "group:workspace";
        nodes[groupId] = new ArchitectureNode(
            groupId,
            ArchitectureNodeKind.ArchitectureGroup,
            new DirectoryInfo(workspaceRoot).Name,
            null,
            null,
            "Live architecture projected from the workspace CodeGraph index.",
            "workspace",
            ["codegraph", "live"],
            []);

        foreach (var project in projects)
        {
            var isExternalPackage = project.Tags.Contains("source:external", StringComparer.Ordinal);
            nodes[project.Id] = new ArchitectureNode(
                project.Id,
                ArchitectureNodeKind.Project,
                project.Name,
                groupId,
                project.RelativePath,
                project.Description,
                isExternalPackage ? "external-package" : "project",
                project.Tags,
                []);
        }

        foreach (var package in packages)
        {
            nodes[package.Id] = new ArchitectureNode(
                package.Id,
                ArchitectureNodeKind.Project,
                package.Name,
                groupId,
                package.Name,
                package.Description,
                "external-package",
                ["dotnet", "project-type:nuget-package", "source:external", "package:nuget", "read-only"],
                []);
        }

        var rawToArchitecture = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawNode in payload.Nodes.Where(node => node.Kind == "namespace")
            .Where(node => !IsSourceUnderNestedRepositoryBoundary(node.FilePath, nestedRepositoryBoundaries)))
        {
            var project = FindProject(projects, rawNode.FilePath);
            var namespaceId = $"namespace:{project.Id}:{rawNode.Name}";
            rawToArchitecture[rawNode.Id] = namespaceId;
            nodes.TryAdd(
                namespaceId,
                new ArchitectureNode(
                    namespaceId,
                    ArchitectureNodeKind.Namespace,
                    rawNode.Name,
                    project.Id,
                    rawNode.Name,
                    ResolveDescription(rawNode, sourceDocumentation),
                    "namespace",
                    [rawNode.Language],
                    [new SourceLocation(rawNode.FilePath, rawNode.StartLine, rawNode.EndLine)]));
        }

        var containingNamespaces = payload.Edges
            .Where(edge => edge.Kind == "contains" && rawToArchitecture.ContainsKey(edge.Source))
            .GroupBy(edge => edge.Target, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => rawToArchitecture[group.First().Source], StringComparer.Ordinal);

        var symbolDeclarations = payload.Nodes.Where(node => node.Kind != "namespace")
            .Where(node => !IsSourceUnderNestedRepositoryBoundary(node.FilePath, nestedRepositoryBoundaries))
            .Select(node => new SymbolDeclaration(node, FindProject(projects, node.FilePath)))
            .GroupBy(CreateLogicalSymbolKey, StringComparer.Ordinal);

        foreach (var declarationGroup in symbolDeclarations)
        {
            var declarations = declarationGroup
                .OrderBy(declaration => declaration.Node.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(declaration => declaration.Node.StartLine)
                .ThenBy(declaration => declaration.Node.Id, StringComparer.Ordinal)
                .ToArray();
            var primary = declarations[0];
            var project = primary.Project;
            var nodeId = CreateArchitectureSymbolId(declarations);
            foreach (var declaration in declarations)
            {
                rawToArchitecture[declaration.Node.Id] = nodeId;
            }

            var parentId = declarations
                .Select(declaration => containingNamespaces.GetValueOrDefault(declaration.Node.Id))
                .FirstOrDefault(candidate => candidate is not null)
                ?? project.Id;
            var description = declarations
                .Select(declaration => ResolveDescription(declaration.Node, sourceDocumentation))
                .FirstOrDefault(candidate => candidate is not null);
            var representative = declarations.FirstOrDefault(declaration => declaration.Node.IsAbstract) ?? primary;
            nodes[nodeId] = new ArchitectureNode(
                nodeId,
                MapNodeKind(representative.Node),
                primary.Node.Name,
                parentId,
                primary.Node.QualifiedName,
                description,
                "symbol",
                [primary.Node.Language, primary.Node.Kind],
                declarations
                    .Select(declaration => new SourceLocation(
                        declaration.Node.FilePath,
                        declaration.Node.StartLine,
                        declaration.Node.EndLine))
                    .Distinct()
                    .ToArray());
        }

        var semanticRelations = payload.Edges
            .Where(edge => edge.Kind != "contains")
            .Where(edge => rawToArchitecture.ContainsKey(edge.Source) && rawToArchitecture.ContainsKey(edge.Target))
            .Select(edge => new
            {
                Source = rawToArchitecture[edge.Source],
                Target = rawToArchitecture[edge.Target],
                Kind = MapRelationKind(edge.Kind),
                Confidence = edge.Provenance == "heuristic" ? EvidenceConfidence.Heuristic : EvidenceConfidence.Exact,
            })
            .Where(edge => edge.Source != edge.Target)
            .GroupBy(edge => (edge.Source, edge.Target, edge.Kind))
            .Select(group => new ArchitectureRelation(
                $"relation:{group.Key.Source}:{group.Key.Target}:{group.Key.Kind}",
                group.Key.Source,
                group.Key.Target,
                group.Key.Kind,
                Math.Min(group.Count(), 12),
                group.Count(),
                group.All(edge => edge.Confidence == EvidenceConfidence.Exact)
                    ? EvidenceConfidence.Exact
                    : EvidenceConfidence.Heuristic))
            .ToList();

        semanticRelations.AddRange(MapProjectReferences(projects));
        semanticRelations.AddRange(MapNuGetPackageReferences(packages));
        semanticRelations.AddRange(MapHttpApiIntegrations(projects
            .Select(project => new HttpApiProject(project.Id, project.RootPath, project.Tags))
            .ToArray()));
        return ArchitectureGraph.Create(nodes.Values, semanticRelations);
    }

    private static List<ProjectInfo> DiscoverProjects(string workspaceRoot)
    {
        var projectFiles = EnumerateProjectManifestPaths(workspaceRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projects = projectFiles.Select(path =>
        {
            var kind = path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? ProjectKind.DotNet
                : ProjectKind.TypeScript;
            var projectRoot = Path.GetDirectoryName(path)!;
            var name = kind == ProjectKind.DotNet
                ? Path.GetFileNameWithoutExtension(path)
                : ReadPackageName(path) ?? new DirectoryInfo(projectRoot).Name;
            var relativePath = Path.GetRelativePath(workspaceRoot, projectRoot).Replace('\\', '/');
            var manifest = kind == ProjectKind.DotNet
                ? XDocument.Load(path, LoadOptions.None)
                : null;
            var classification = manifest is null
                ? null
                : DotNetProjectClassifier.Classify(manifest, name);
            var packageTags = kind == ProjectKind.TypeScript
                ? ClassifyPackageJsonTags(workspaceRoot, path)
                : null;
            var declaredDescription = manifest is null
                ? ReadPackageProperty(path, "description")
                : DocumentationSummaryExtractor.FromProjectManifest(manifest);
            return new ProjectInfo(
                $"project:{relativePath}:{name}",
                name,
                projectRoot,
                relativePath,
                path,
                kind,
                declaredDescription
                    ?? classification?.Description
                    ?? (packageTags?.Contains("package:unity", StringComparer.Ordinal) == true
                        ? "Unity package"
                        : "TypeScript package"),
                classification is null
                    ? packageTags!
                    : ["dotnet", classification.TypeTag, .. classification.CapabilityTags],
                manifest);
        }).ToList();

        if (projects.Count == 0)
        {
            projects.Add(new ProjectInfo(
                "project:workspace",
                new DirectoryInfo(workspaceRoot).Name,
                workspaceRoot,
                ".",
                string.Empty,
                ProjectKind.Other,
                "Workspace sources",
                ["project-type:workspace"],
                null));
        }

        return projects;
    }

    internal static IReadOnlyList<string> EnumerateProjectManifestPaths(string workspaceRoot) =>
        EnumerateWorkspaceFiles(workspaceRoot, stopAtNestedRepositoryBoundaries: true)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    /// <summary>
    /// Classifies package manifests by ownership evidence rather than treating every package.json as
    /// first-party TypeScript. Unity's Library/PackageCache is generated, read-only dependency state;
    /// embedded packages under Packages remain workspace-owned and editable.
    /// </summary>
    internal static IReadOnlyList<string> ClassifyPackageJsonTags(string workspaceRoot, string packagePath)
    {
        var relativePath = Path.GetRelativePath(
                Path.GetFullPath(workspaceRoot),
                Path.GetFullPath(packagePath))
            .Replace('\\', '/');
        var isUnityPackageCacheManifest =
            Path.GetFileName(packagePath).Equals("package.json", StringComparison.OrdinalIgnoreCase)
            && relativePath.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase);

        return isUnityPackageCacheManifest
            ? ["unity", "project-type:unity-package", "package:unity", "source:external", "read-only"]
            : ["typescript", "project-type:typescript"];
    }

    internal static IReadOnlyList<string> EnumerateNestedRepositoryBoundaryPaths(string workspaceRoot)
    {
        var boundaries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);

        while (pending.TryPop(out var directory))
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (IgnoredDirectoryNames.Contains(Path.GetFileName(child)))
                {
                    continue;
                }

                if (DefinesNestedRepositoryBoundary(child))
                {
                    boundaries.Add(Path.GetRelativePath(workspaceRoot, child).Replace('\\', '/'));
                    continue;
                }

                pending.Push(child);
            }
        }

        return boundaries;
    }

    internal static bool IsSourceUnderNestedRepositoryBoundary(
        string relativeFilePath,
        IReadOnlyList<string> boundaryPaths)
    {
        var normalized = relativeFilePath.Replace('\\', '/');
        return boundaryPaths.Any(boundary =>
            normalized.Equals(boundary, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(boundary + '/', StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(
        string workspaceRoot,
        bool stopAtNestedRepositoryBoundaries = false)
    {
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);

        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!IgnoredDirectoryNames.Contains(Path.GetFileName(child))
                    && !(stopAtNestedRepositoryBoundaries && DefinesNestedRepositoryBoundary(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static bool DefinesNestedRepositoryBoundary(string directory) =>
        Directory.Exists(Path.Combine(directory, ".git"))
        || File.Exists(Path.Combine(directory, ".git"));

    private static string CreateLogicalSymbolKey(SymbolDeclaration declaration) =>
        declaration.Node.Language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(declaration.Node.QualifiedName)
            ? string.Join('\u001f', declaration.Project.Id, declaration.Node.Kind, declaration.Node.QualifiedName)
            : string.Join('\u001f', declaration.Project.Id, declaration.Node.Id);

    private static string CreateArchitectureSymbolId(IReadOnlyList<SymbolDeclaration> declarations)
    {
        var primary = declarations[0];
        return declarations.Count > 1
            ? $"symbol:{primary.Project.Id}:{primary.Node.Kind}:{primary.Node.QualifiedName}"
            : $"symbol:{primary.Node.Id}";
    }

    private static List<NuGetPackageInfo> DiscoverNuGetPackages(IReadOnlyList<ProjectInfo> projects)
    {
        var packages = new Dictionary<string, NuGetPackageAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects.Where(project => project.Kind == ProjectKind.DotNet))
        {
            var manifest = project.Manifest
                ?? throw new InvalidOperationException($"The .NET project '{project.Name}' has no parsed manifest.");
            foreach (var packageReference in ReadNuGetPackageReferences(manifest))
            {
                if (!packages.TryGetValue(packageReference.PackageId, out var package))
                {
                    package = new NuGetPackageAccumulator(packageReference.PackageId);
                    packages.Add(packageReference.PackageId, package);
                }

                package.ConsumerProjectIds.Add(project.Id);
                if (!string.IsNullOrWhiteSpace(packageReference.Version))
                {
                    package.Versions.Add(packageReference.Version);
                }
            }
        }

        return packages.Values
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .Select(package => new NuGetPackageInfo(
                $"package:nuget:{package.Name}",
                package.Name,
                package.Versions.Count == 1
                    ? $"NuGet package {package.Versions.Single()} · read-only"
                    : "NuGet package · read-only",
                package.ConsumerProjectIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
            .ToList();
    }

    internal static IReadOnlyList<NuGetPackageReference> ReadNuGetPackageReferences(XDocument manifest) =>
        manifest.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => new NuGetPackageReference(
                element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? string.Empty,
                element.Attribute("Version")?.Value
                    ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value))
            .Where(package => !string.IsNullOrWhiteSpace(package.PackageId))
            .Select(package => package with
            {
                PackageId = package.PackageId.Trim(),
                Version = string.IsNullOrWhiteSpace(package.Version) ? null : package.Version.Trim(),
            })
            .ToArray();

    private static ProjectInfo FindProject(IReadOnlyList<ProjectInfo> projects, string relativeFilePath)
    {
        var normalized = relativeFilePath.Replace('\\', '/');
        return projects
            .Where(project => normalized.Equals(project.RelativePath, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(project.RelativePath + '/', StringComparison.OrdinalIgnoreCase)
                || project.RelativePath == ".")
            .OrderByDescending(project => project.RelativePath.Length)
            .FirstOrDefault()
            ?? projects[0];
    }

    private static IEnumerable<ArchitectureRelation> MapProjectReferences(IReadOnlyList<ProjectInfo> projects)
    {
        var byProjectFile = projects
            .Where(project => project.Kind == ProjectKind.DotNet)
            .ToDictionary(project => Path.GetFullPath(project.ManifestPath), StringComparer.OrdinalIgnoreCase);

        foreach (var project in byProjectFile.Values)
        {
            var document = project.Manifest
                ?? throw new InvalidOperationException($"The .NET project '{project.Name}' has no parsed manifest.");
            foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "ProjectReference"))
            {
                var include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(project.RootPath, include));
                if (byProjectFile.TryGetValue(targetPath, out var target))
                {
                    yield return new ArchitectureRelation(
                        $"project-reference:{project.Id}:{target.Id}",
                        project.Id,
                        target.Id,
                        ArchitectureRelationKind.ProjectReference,
                        4,
                        1,
                        EvidenceConfidence.Exact);
                }
            }
        }
    }

    private static IEnumerable<ArchitectureRelation> MapNuGetPackageReferences(
        IReadOnlyList<NuGetPackageInfo> packages)
    {
        foreach (var package in packages)
        {
            foreach (var consumerProjectId in package.ConsumerProjectIds)
            {
                yield return new ArchitectureRelation(
                    $"nuget-reference:{consumerProjectId}:{package.Id}",
                    consumerProjectId,
                    package.Id,
                    ArchitectureRelationKind.ProjectReference,
                    3,
                    1,
                    EvidenceConfidence.Exact);
            }
        }
    }

    internal static IReadOnlyList<ArchitectureRelation> MapHttpApiIntegrations(
        IReadOnlyList<HttpApiProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var consumers = projects
            .Where(project => project.Tags.Contains("project-type:typescript", StringComparer.OrdinalIgnoreCase))
            .Select(project => (Project: project, Routes: ReadHttpApiRoutes(project.RootPath, isProvider: false)))
            .Where(item => item.Routes.Count > 0)
            .ToArray();
        var providers = projects
            .Where(project => project.Tags.Contains("protocol:rest", StringComparer.OrdinalIgnoreCase))
            .Select(project => (Project: project, Routes: ReadHttpApiRoutes(project.RootPath, isProvider: true)))
            .Where(item => item.Routes.Count > 0)
            .ToArray();
        var relations = new List<ArchitectureRelation>();

        foreach (var consumer in consumers)
        {
            foreach (var provider in providers)
            {
                var matchedRoutes = consumer.Routes
                    .Intersect(provider.Routes, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (matchedRoutes.Length == 0)
                {
                    continue;
                }

                relations.Add(new ArchitectureRelation(
                    $"http-api:{consumer.Project.Id}:{provider.Project.Id}",
                    consumer.Project.Id,
                    provider.Project.Id,
                    ArchitectureRelationKind.HttpApi,
                    Math.Min(matchedRoutes.Length + 2, 6),
                    matchedRoutes.Length,
                    EvidenceConfidence.Inferred));
            }
        }

        return relations;
    }

    private static HashSet<string> ReadHttpApiRoutes(string projectRoot, bool isProvider)
    {
        var extensions = isProvider
            ? new HashSet<string>([".cs"], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>([".ts", ".tsx", ".js", ".jsx"], StringComparer.OrdinalIgnoreCase);
        var pattern = isProvider ? HttpApiProviderRoutePattern : HttpApiConsumerRoutePattern;
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateWorkspaceFiles(projectRoot)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .Where(path => isProvider || !IsTypeScriptTestSource(path)))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                var route = match.Groups["route"].Value.TrimEnd('/');
                routes.Add(route.Length == 0 ? "/" : route);
            }
        }

        return routes;
    }

    private static bool IsTypeScriptTestSource(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase);
    }

    private static ArchitectureNodeKind MapNodeKind(CodeGraphNode node) => node.Kind switch
    {
        "interface" => ArchitectureNodeKind.Interface,
        "class" when node.IsAbstract => ArchitectureNodeKind.AbstractClass,
        _ => ArchitectureNodeKind.Class,
    };

    private static ArchitectureRelationKind MapRelationKind(string kind) => kind switch
    {
        "extends" => ArchitectureRelationKind.Inherits,
        "implements" => ArchitectureRelationKind.Implements,
        _ => ArchitectureRelationKind.DependsOn,
    };

    private static string? ReadPackageName(string packagePath) => ReadPackageProperty(packagePath, "name");

    private static string? ReadPackageProperty(string packagePath, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
        return document.RootElement.TryGetProperty(propertyName, out var property)
            ? DocumentationSummaryExtractor.Normalize(property.GetString())
            : null;
    }

    private static string? ResolveDescription(
        CodeGraphNode node,
        CSharpDocumentationSummaryReader sourceDocumentation) =>
        DocumentationSummaryExtractor.FromDocstring(node.Docstring)
        ?? sourceDocumentation.Read(node.FilePath, node.StartLine);

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codegraph", "bin", "obj", "node_modules", "artifacts", "wwwroot", "dist", "server",
    };

    private sealed record ProjectInfo(
        string Id,
        string Name,
        string RootPath,
        string RelativePath,
        string ManifestPath,
        ProjectKind Kind,
        string Description,
        IReadOnlyList<string> Tags,
        XDocument? Manifest);

    private sealed record SymbolDeclaration(CodeGraphNode Node, ProjectInfo Project);

    private sealed record NuGetPackageInfo(
        string Id,
        string Name,
        string Description,
        IReadOnlyList<string> ConsumerProjectIds);

    private sealed class NuGetPackageAccumulator(string name)
    {
        public string Name { get; } = name;

        public HashSet<string> ConsumerProjectIds { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private enum ProjectKind
    {
        Other,
        DotNet,
        TypeScript,
    }

    internal sealed class CodeGraphPayload
    {
        public IReadOnlyList<CodeGraphNode> Nodes { get; init; } = [];

        public IReadOnlyList<CodeGraphEdge> Edges { get; init; } = [];
    }

    internal sealed class CodeGraphNode
    {
        public required string Id { get; init; }

        public required string Kind { get; init; }

        public required string Name { get; init; }

        public required string QualifiedName { get; init; }

        public required string FilePath { get; init; }

        public required string Language { get; init; }

        public int StartLine { get; init; }

        public int EndLine { get; init; }

        public string? Docstring { get; init; }

        public bool IsAbstract { get; init; }
    }

    internal sealed class CodeGraphEdge
    {
        public required string Source { get; init; }

        public required string Target { get; init; }

        public required string Kind { get; init; }

        public string? Provenance { get; init; }
    }
}

internal sealed record HttpApiProject(
    string Id,
    string RootPath,
    IReadOnlyList<string> Tags);

internal sealed record NuGetPackageReference(string PackageId, string? Version);
