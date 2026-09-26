using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Cave.Application;
using Cave.Domain;

namespace Cave.Infrastructure.Git;

/// <summary>
/// Reads deterministic working-tree changes through the native Git command-line contract.
/// </summary>
public sealed partial class NativeGitDeltaProvider : IGitDeltaProvider
{
    /// <inheritdoc />
    public async Task<GitDeltaResult> ReadAsync(
        string workspaceRoot,
        GitBaselineRequest baseline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var canonicalRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new GitDeltaUnavailableException(
                $"Git workspace root '{canonicalRoot}' does not exist.");
        }

        var resolvedBaseline = await ResolveBaselineAsync(canonicalRoot, baseline, cancellationToken)
            .ConfigureAwait(false);
        var worktreeIdentity = await ReadWorktreeIdentityAsync(canonicalRoot, cancellationToken)
            .ConfigureAwait(false);
        var statuses = ParseNameStatus(await RunGitAsync(
            canonicalRoot,
            ["diff", "--relative", "--name-status", "-z", "--find-renames", resolvedBaseline.ResolvedSha, "--", "."],
            cancellationToken).ConfigureAwait(false));
        var statistics = ParseNumStat(await RunGitAsync(
            canonicalRoot,
            ["diff", "--relative", "--numstat", "-z", "--find-renames", resolvedBaseline.ResolvedSha, "--", "."],
            cancellationToken).ConfigureAwait(false));

        var files = new List<GitFileDelta>();
        foreach (var status in statuses)
        {
            var statistic = statistics.GetValueOrDefault(status.FilePath);
            var hunks = statistic is null || statistic.IsBinary
                ? []
                : ParseHunks(await RunGitAsync(
                    canonicalRoot,
                    ["diff", "--relative", "--unified=0", "--no-ext-diff", "--no-color", resolvedBaseline.ResolvedSha, "--", status.FilePath],
                    cancellationToken).ConfigureAwait(false));
            files.Add(new GitFileDelta(
                status.FilePath,
                status.PreviousPath,
                status.Kind,
                statistic?.Additions ?? 0,
                statistic?.Deletions ?? 0,
                statistic?.IsBinary ?? false,
                hunks));
        }

        var untrackedOutput = await RunGitAsync(
            canonicalRoot,
            ["ls-files", "--others", "--exclude-standard", "-z", "--", "."],
            cancellationToken).ConfigureAwait(false);
        foreach (var path in SplitNullTerminated(untrackedOutput))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = NormalizePath(path);
            var absolutePath = Path.GetFullPath(Path.Combine(canonicalRoot, normalizedPath));
            if (!absolutePath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(absolutePath))
            {
                continue;
            }

            var isBinary = IsBinaryFile(absolutePath);
            var additions = isBinary ? 0 : CountLines(absolutePath, cancellationToken);
            files.Add(new GitFileDelta(
                normalizedPath,
                null,
                GitFileChangeKind.Untracked,
                additions,
                0,
                isBinary,
                isBinary || additions == 0
                    ? []
                    : [new GitHunkDelta(0, 0, 1, additions)]));
        }

        return new GitDeltaResult(
            resolvedBaseline,
            worktreeIdentity,
            files.OrderBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task<GitWorktreeIdentity> ReadWorktreeIdentityAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var headSha = (await RunGitAsync(
            workspaceRoot,
            ["rev-parse", "--verify", "HEAD^{commit}"],
            cancellationToken).ConfigureAwait(false)).Trim();
        var branch = (await RunGitAsync(
            workspaceRoot,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            cancellationToken).ConfigureAwait(false)).Trim();

        return new GitWorktreeIdentity(
            headSha,
            branch.Equals("HEAD", StringComparison.Ordinal) ? null : branch);
    }

    private static async Task<GitBaseline> ResolveBaselineAsync(
        string workspaceRoot,
        GitBaselineRequest request,
        CancellationToken cancellationToken)
    {
        var requestedReference = request.Kind switch
        {
            GitBaselineKind.Upstream => "@{upstream}",
            GitBaselineKind.Head => "HEAD",
            GitBaselineKind.Commit when !string.IsNullOrWhiteSpace(request.Commit) => request.Commit,
            GitBaselineKind.Commit => throw new GitDeltaUnavailableException(
                "The configured commit baseline requires a Git revision."),
            _ => throw new InvalidOperationException($"Unsupported Git baseline kind '{request.Kind}'."),
        };
        var resolvedSha = (await RunGitAsync(
            workspaceRoot,
            ["rev-parse", "--verify", $"{requestedReference}^{{commit}}"],
            cancellationToken).ConfigureAwait(false)).Trim();
        var displayReference = request.Kind == GitBaselineKind.Upstream
            ? (await RunGitAsync(
                workspaceRoot,
                ["rev-parse", "--abbrev-ref", "--symbolic-full-name", requestedReference],
                cancellationToken).ConfigureAwait(false)).Trim()
            : requestedReference;

        return new GitBaseline(request.Kind, displayReference, resolvedSha);
    }

    private static async Task<string> RunGitAsync(
        string workspaceRoot,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(workspaceRoot, arguments);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitDeltaUnavailableException("The native Git process could not be started.");
            }

            // CONSTRAINT: CAVE can run over an MCP stdio transport. Native children must never inherit
            // that input handle because a child read would compete with or indefinitely hold JSON-RPC input.
            process.StandardInput.Close();
        }
        catch (Win32Exception exception)
        {
            throw new GitDeltaUnavailableException(
                "The native Git executable is unavailable on this machine.",
                exception);
        }

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
            var command = string.Join(' ', arguments.Take(2));
            throw new GitDeltaUnavailableException(
                $"Git {command} failed for '{workspaceRoot}': {error.Trim()}");
        }

        return output;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string workspaceRoot,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspaceRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static List<GitFileStatus> ParseNameStatus(string output)
    {
        var tokens = SplitNullTerminated(output);
        var files = new List<GitFileStatus>();
        for (var index = 0; index < tokens.Length;)
        {
            var status = tokens[index++];
            if (string.IsNullOrEmpty(status) || index >= tokens.Length)
            {
                break;
            }

            var code = status[0];
            if ((code == 'R' || code == 'C') && index + 1 < tokens.Length)
            {
                var previousPath = NormalizePath(tokens[index++]);
                var currentPath = NormalizePath(tokens[index++]);
                files.Add(new GitFileStatus(
                    currentPath,
                    previousPath,
                    code == 'R' ? GitFileChangeKind.Renamed : GitFileChangeKind.Added));
                continue;
            }

            var filePath = NormalizePath(tokens[index++]);
            files.Add(new GitFileStatus(filePath, null, code switch
            {
                'A' => GitFileChangeKind.Added,
                'D' => GitFileChangeKind.Deleted,
                _ => GitFileChangeKind.Modified,
            }));
        }

        return files;
    }

    private static Dictionary<string, GitFileStatistic> ParseNumStat(string output)
    {
        var tokens = SplitNullTerminated(output);
        var statistics = new Dictionary<string, GitFileStatistic>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Length; index++)
        {
            var parts = tokens[index].Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            string path;
            if (string.IsNullOrEmpty(parts[2]) && index + 2 < tokens.Length)
            {
                index += 2;
                path = tokens[index];
            }
            else
            {
                path = parts[2];
            }

            var isBinary = parts[0] == "-" || parts[1] == "-";
            statistics[NormalizePath(path)] = new GitFileStatistic(
                isBinary ? 0 : int.Parse(parts[0], CultureInfo.InvariantCulture),
                isBinary ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture),
                isBinary);
        }

        return statistics;
    }

    private static List<GitHunkDelta> ParseHunks(string patch)
    {
        var hunks = new List<GitHunkDelta>();
        foreach (var line in patch.Split('\n'))
        {
            var match = HunkHeader().Match(line);
            if (!match.Success)
            {
                continue;
            }

            hunks.Add(new GitHunkDelta(
                ParseInt(match.Groups["oldStart"].Value),
                ParseCount(match.Groups["oldCount"].Value),
                ParseInt(match.Groups["newStart"].Value),
                ParseCount(match.Groups["newCount"].Value)));
        }

        return hunks;
    }

    private static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static int ParseCount(string value) => string.IsNullOrEmpty(value) ? 1 : ParseInt(value);

    private static string[] SplitNullTerminated(string value) =>
        value.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsBinaryFile(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[8192];
        var read = stream.Read(buffer);
        return buffer[..read].Contains((byte)0);
    }

    private static int CountLines(string path, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var _ in File.ReadLines(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
        }

        return count;
    }

    [GeneratedRegex(
        "^@@ -(?<oldStart>\\d+)(?:,(?<oldCount>\\d+))? \\+(?<newStart>\\d+)(?:,(?<newCount>\\d+))? @@",
        RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    private sealed record GitFileStatus(
        string FilePath,
        string? PreviousPath,
        GitFileChangeKind Kind);

    private sealed record GitFileStatistic(
        int Additions,
        int Deletions,
        bool IsBinary);
}
