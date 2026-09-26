using Cave.Domain;

namespace Cave.Application;

/// <summary>
/// Projects raw Git hunks onto semantic nodes and their architecture ancestors.
/// </summary>
/// <param name="provider">The configured Git evidence provider.</param>
/// <param name="baseline">The single configured comparison baseline.</param>
public sealed class GitDeltaService(
    IGitDeltaProvider provider,
    GitBaselineRequest baseline)
{
    /// <summary>
    /// Reads and projects the current Git delta without mutating the base graph.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <param name="graph">The current normalized architecture graph.</param>
    /// <param name="cancellationToken">Signals that the operation should stop.</param>
    /// <returns>A ready overlay, or an explicit unavailable overlay for an expected Git boundary failure.</returns>
    public async Task<GitDeltaOverlay> GetAsync(
        string workspaceRoot,
        ArchitectureGraph graph,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await provider.ReadAsync(workspaceRoot, baseline, cancellationToken)
                .ConfigureAwait(false);
            return Project(result, graph);
        }
        catch (GitDeltaUnavailableException exception)
        {
            return new GitDeltaOverlay(
                GitDeltaStatus.Unavailable,
                null,
                null,
                [],
                [],
                [],
                exception.Message);
        }
    }

    private static GitDeltaOverlay Project(GitDeltaResult result, ArchitectureGraph graph)
    {
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var direct = new Dictionary<string, MutableNodeDelta>(StringComparer.Ordinal);
        var unmapped = new List<GitFileDelta>();

        foreach (var file in result.Files)
        {
            var normalizedPath = NormalizePath(file.FilePath);
            var sourceNodes = graph.Nodes
                .Where(node => node.SourceLocations.Any(location =>
                    NormalizePath(location.FilePath).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var project = FindProject(graph.Nodes, normalizedPath);
            var group = graph.Nodes.FirstOrDefault(node =>
                node.Kind == ArchitectureNodeKind.ArchitectureGroup && node.ParentId is null);
            var mappedBeyondGroup = false;

            if (file.Hunks.Count == 0)
            {
                var target = project ?? group;
                if (target is not null)
                {
                    AddDirect(direct, target.Id, file, file.Additions, file.Deletions);
                    mappedBeyondGroup = target.Kind != ArchitectureNodeKind.ArchitectureGroup;
                }
            }
            else
            {
                foreach (var hunk in file.Hunks)
                {
                    var additionTargets = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (var offset = 0; offset < hunk.NewCount; offset++)
                    {
                        var target = FindSourceNode(sourceNodes, hunk.NewStart + offset)
                            ?? project
                            ?? group;
                        if (target is null)
                        {
                            continue;
                        }

                        additionTargets[target.Id] = additionTargets.GetValueOrDefault(target.Id) + 1;
                        mappedBeyondGroup |= target.Kind != ArchitectureNodeKind.ArchitectureGroup;
                    }

                    foreach (var assignment in additionTargets)
                    {
                        AddDirect(direct, assignment.Key, file, assignment.Value, 0);
                    }

                    AssignDeletions(
                        direct,
                        file,
                        hunk,
                        additionTargets,
                        sourceNodes,
                        project,
                        group,
                        ref mappedBeyondGroup);
                }
            }

            if (!mappedBeyondGroup)
            {
                unmapped.Add(file);
            }
        }

        var aggregated = new Dictionary<string, MutableNodeDelta>(StringComparer.Ordinal);
        foreach (var entry in direct)
        {
            string? currentId = entry.Key;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (currentId is not null && visited.Add(currentId) && nodesById.TryGetValue(currentId, out var node))
            {
                var target = GetMutable(aggregated, currentId);
                target.Add(entry.Value);
                currentId = node.ParentId;
            }
        }

        var nodeDeltas = aggregated
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value.ToRecord(entry.Key))
            .ToArray();

        return new GitDeltaOverlay(
            GitDeltaStatus.Ready,
            result.Baseline,
            result.Worktree,
            result.Files,
            nodeDeltas,
            unmapped,
            null);
    }

    private static void AssignDeletions(
        IDictionary<string, MutableNodeDelta> direct,
        GitFileDelta file,
        GitHunkDelta hunk,
        Dictionary<string, int> additionTargets,
        IReadOnlyList<ArchitectureNode> sourceNodes,
        ArchitectureNode? project,
        ArchitectureNode? group,
        ref bool mappedBeyondGroup)
    {
        if (hunk.OldCount == 0)
        {
            return;
        }

        if (additionTargets.Count == 0)
        {
            var target = FindSourceNode(sourceNodes, Math.Max(1, hunk.NewStart)) ?? project ?? group;
            if (target is not null)
            {
                AddDirect(direct, target.Id, file, 0, hunk.OldCount);
                mappedBeyondGroup |= target.Kind != ArchitectureNodeKind.ArchitectureGroup;
            }

            return;
        }

        var assignments = additionTargets.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var additions = assignments.Sum(entry => entry.Value);
        var remaining = hunk.OldCount;
        for (var index = 0; index < assignments.Length; index++)
        {
            var deletionCount = index == assignments.Length - 1
                ? remaining
                : (int)Math.Floor((double)hunk.OldCount * assignments[index].Value / additions);
            remaining -= deletionCount;
            AddDirect(direct, assignments[index].Key, file, 0, deletionCount);
        }
    }

    private static ArchitectureNode? FindSourceNode(
        IEnumerable<ArchitectureNode> nodes,
        int line) =>
        nodes
            .SelectMany(node => node.SourceLocations.Select(location => (Node: node, Location: location)))
            .Where(candidate => line >= candidate.Location.StartLine && line <= candidate.Location.EndLine)
            .OrderBy(candidate => NodePriority(candidate.Node.Kind))
            .ThenBy(candidate => candidate.Location.EndLine - candidate.Location.StartLine)
            .Select(candidate => candidate.Node)
            .FirstOrDefault();

    private static ArchitectureNode? FindProject(
        IEnumerable<ArchitectureNode> nodes,
        string path) =>
        nodes
            .Where(node => node.Kind == ArchitectureNodeKind.Project)
            .Select(node => (Node: node, Root: NormalizePath(node.QualifiedName ?? string.Empty).TrimEnd('/')))
            .Where(candidate => candidate.Root == "."
                || path.Equals(candidate.Root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(candidate.Root + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Root.Length)
            .Select(candidate => candidate.Node)
            .FirstOrDefault();

    private static int NodePriority(ArchitectureNodeKind kind) => kind switch
    {
        ArchitectureNodeKind.Class => 0,
        ArchitectureNodeKind.Interface => 0,
        ArchitectureNodeKind.AbstractClass => 0,
        ArchitectureNodeKind.Namespace => 1,
        _ => 2,
    };

    private static void AddDirect(
        IDictionary<string, MutableNodeDelta> deltas,
        string nodeId,
        GitFileDelta file,
        int additions,
        int deletions) =>
        GetMutable(deltas, nodeId).Add(file, additions, deletions);

    private static MutableNodeDelta GetMutable(
        IDictionary<string, MutableNodeDelta> deltas,
        string nodeId)
    {
        if (!deltas.TryGetValue(nodeId, out var delta))
        {
            delta = new MutableNodeDelta();
            deltas[nodeId] = delta;
        }

        return delta;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed class MutableNodeDelta
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<GitStructuralChangeKind> _kinds = [];

        public int Additions { get; private set; }

        public int Deletions { get; private set; }

        public void Add(GitFileDelta file, int additions, int deletions)
        {
            Additions += additions;
            Deletions += deletions;
            _paths.Add(file.FilePath);
            _kinds.Add(ToStructuralKind(file.Kind));
        }

        public void Add(MutableNodeDelta source)
        {
            Additions += source.Additions;
            Deletions += source.Deletions;
            _paths.UnionWith(source._paths);
            _kinds.UnionWith(source._kinds);
        }

        public GitNodeDelta ToRecord(string nodeId) => new(
            nodeId,
            _kinds.Count == 1 ? _kinds.Single() : GitStructuralChangeKind.Mixed,
            Additions,
            Deletions,
            _paths.Count,
            _paths.Order(StringComparer.OrdinalIgnoreCase).ToArray());

        private static GitStructuralChangeKind ToStructuralKind(GitFileChangeKind kind) => kind switch
        {
            GitFileChangeKind.Added => GitStructuralChangeKind.Added,
            GitFileChangeKind.Untracked => GitStructuralChangeKind.Added,
            GitFileChangeKind.Deleted => GitStructuralChangeKind.Deleted,
            _ => GitStructuralChangeKind.Modified,
        };
    }
}
