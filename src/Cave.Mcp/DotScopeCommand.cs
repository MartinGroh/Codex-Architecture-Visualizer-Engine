using System.Text.Json;
using Cave.Application;
using Cave.Domain;

namespace Cave.Mcp;

/// <summary>
/// Records an explicitly declared Dot task scope without requiring a Codex MCP session.
/// </summary>
public static class DotScopeCommand
{
    private const int MaxInputLength = 8192;
    private const int MaxTaskIdLength = 120;
    private const int MaxSummaryLength = 160;
    private const int MaxFiles = 32;

    /// <summary>
    /// Reads one JSON declaration from standard input and writes it through CAVE's activity store.
    /// This command does not claim that Codex hooks observed the Dot task.
    /// </summary>
    /// <param name="input">The JSON input stream.</param>
    /// <param name="output">The machine-readable result stream.</param>
    /// <param name="error">The diagnostic stream.</param>
    /// <param name="activityStore">The canonical activity journal.</param>
    /// <param name="catalog">The machine workspace catalog.</param>
    /// <param name="cancellationToken">Signals that the command should stop.</param>
    /// <returns>Zero on success, or two for invalid input or a failed publication.</returns>
    public static async Task<int> RunAsync(
        TextReader input,
        TextWriter output,
        TextWriter error,
        IAgentActivityStore activityStore,
        IWorkspaceCatalogStore catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(activityStore);
        ArgumentNullException.ThrowIfNull(catalog);

        try
        {
            var buffer = new char[MaxInputLength + 1];
            var length = await input.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (length == 0 || length > MaxInputLength)
            {
                throw new ArgumentException("Expected one JSON declaration of at most 8192 characters on standard input.");
            }

            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var request = document.RootElement;
            if (request.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("The declaration must be a JSON object.");
            }

            var workspacePath = RequiredString(request, "workspacePath");
            if (!Path.IsPathFullyQualified(workspacePath))
            {
                throw new ArgumentException("workspacePath must be an absolute path.");
            }

            var workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
            if (!Directory.Exists(workspaceRoot))
            {
                throw new DirectoryNotFoundException($"Workspace root '{workspaceRoot}' does not exist.");
            }

            var gitEntry = Path.Combine(workspaceRoot, ".git");
            if (!Directory.Exists(gitEntry) && !File.Exists(gitEntry))
            {
                throw new ArgumentException("workspacePath must be the exact root of a Git checkout or worktree.");
            }

            var taskId = RequiredString(request, "taskId");
            if (taskId.Length > MaxTaskIdLength || taskId.Any(char.IsWhiteSpace)
                || taskId.Any(char.IsControl))
            {
                throw new ArgumentException("taskId must be at most 120 characters and contain no whitespace or control characters.");
            }

            var stateText = RequiredString(request, "state");
            if (!Enum.TryParse<AgentWorkState>(stateText, ignoreCase: true, out var state)
                || !Enum.IsDefined(state))
            {
                throw new ArgumentException("state must be Planned, Active, Idle, or Completed.");
            }

            var summary = OptionalString(request, "summary");
            if (summary is not null && (summary.Length > MaxSummaryLength || summary.Any(char.IsControl)))
            {
                throw new ArgumentException("summary must be a single line of at most 160 characters.");
            }

            var files = ReadFiles(request, workspaceRoot);
            var declaration = new AgentScopeDeclaration(
                $"dot:{taskId}",
                "Dot",
                IsSubagent: false,
                state,
                summary,
                [],
                [],
                [],
                files)
            {
                BindToObservedMainTask = false,
            };

            await catalog.RegisterAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            await activityStore.AppendScopeAsync(workspaceRoot, declaration, cancellationToken)
                .ConfigureAwait(false);

            var indexed = File.Exists(Path.Combine(workspaceRoot, ".codegraph", "codegraph.db"));
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                status = "recorded",
                workspaceRoot,
                agentId = declaration.AgentId,
                state = state.ToString(),
                architectureIndexed = indexed,
            })).ConfigureAwait(false);
            if (!indexed)
            {
                await error.WriteLineAsync("CAVE recorded the declared scope, but this worktree needs /cave:init for a navigable architecture graph.")
                    .ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or IOException
                                         or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"CAVE dot-scope: {exception.Message}").ConfigureAwait(false);
            return 2;
        }
    }

    private static string RequiredString(JsonElement request, string name)
    {
        if (!request.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"{name} is required as a nonempty string.");
        }

        return property.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement request, string name)
    {
        if (!request.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"{name} must be a string when supplied.");
        }

        return string.IsNullOrWhiteSpace(property.GetString()) ? null : property.GetString()!.Trim();
    }

    private static string[] ReadFiles(JsonElement request, string workspaceRoot)
    {
        if (!request.TryGetProperty("files", out var property))
        {
            return [];
        }

        if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() > MaxFiles)
        {
            throw new ArgumentException("files must be an array of at most 32 workspace-relative paths.");
        }

        var files = new List<string>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ArgumentException("Every file must be a nonempty workspace-relative path.");
            }

            var path = item.GetString()!.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(path) || path.Split('/').Any(part => part is "" or "." or ".."))
            {
                throw new ArgumentException("Every file must stay within the exact workspace root.");
            }

            var absolute = Path.GetFullPath(Path.Combine(workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = workspaceRoot + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new ArgumentException("Every file must stay within the exact workspace root.");
            }

            if (!files.Contains(path, comparer))
            {
                files.Add(path);
            }
        }

        return [.. files];
    }
}
