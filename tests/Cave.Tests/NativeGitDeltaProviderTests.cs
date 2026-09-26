using System.Diagnostics;
using Cave.Application;
using Cave.Domain;
using Cave.Infrastructure.Git;

namespace Cave.Tests;

/// <summary>
/// Verifies the native Git adapter against a real isolated repository.
/// </summary>
public sealed class NativeGitDeltaProviderTests
{
    /// <summary>
    /// Verifies that native Git cannot inherit and consume an owning MCP stdio transport.
    /// </summary>
    [Fact]
    public void GitProcessUsesClosedPrivateStandardInput()
    {
        var startInfo = NativeGitDeltaProvider.CreateStartInfo(
            Path.GetTempPath(),
            ["rev-parse", "--verify", "HEAD^{commit}"]);

        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
    }

    /// <summary>
    /// Verifies tracked hunks and untracked files relative to an exact HEAD baseline.
    /// </summary>
    [Fact]
    public async Task ReadAsyncReturnsTrackedAndUntrackedChanges()
    {
        var repository = Path.Combine(Path.GetTempPath(), "cave-git-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);

        try
        {
            RunGit(repository, "init");
            RunGit(repository, "config", "user.email", "cave-tests@example.invalid");
            RunGit(repository, "config", "user.name", "CAVE Tests");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Tracked.cs"),
                "alpha\nbeta\n",
                CancellationToken.None);
            RunGit(repository, "add", "Tracked.cs");
            RunGit(repository, "commit", "-m", "baseline");

            await File.WriteAllTextAsync(
                Path.Combine(repository, "Tracked.cs"),
                "alpha\nbeta changed\ngamma\n",
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Untracked.ts"),
                "one\ntwo\n",
                CancellationToken.None);

            var result = await new NativeGitDeltaProvider().ReadAsync(
                repository,
                new GitBaselineRequest(GitBaselineKind.Head, null),
                CancellationToken.None);

            Assert.Equal(GitBaselineKind.Head, result.Baseline.Kind);
            Assert.Equal(40, result.Baseline.ResolvedSha.Length);
            Assert.Equal(40, result.Worktree.HeadSha.Length);
            Assert.False(string.IsNullOrWhiteSpace(result.Worktree.Branch));
            var tracked = Assert.Single(result.Files, file => file.FilePath == "Tracked.cs");
            Assert.Equal(GitFileChangeKind.Modified, tracked.Kind);
            Assert.Equal(2, tracked.Additions);
            Assert.Equal(1, tracked.Deletions);
            Assert.NotEmpty(tracked.Hunks);
            var untracked = Assert.Single(result.Files, file => file.FilePath == "Untracked.ts");
            Assert.Equal(GitFileChangeKind.Untracked, untracked.Kind);
            Assert.Equal(2, untracked.Additions);
        }
        finally
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(
                repository,
                "*",
                SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Directory.Delete(repository, recursive: true);
        }
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git did not start.");
        process.WaitForExit();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error}");
    }
}
