using Cave.Domain;
using Cave.Infrastructure.Codex;

namespace Cave.Tests;

/// <summary>Serializes process-environment changes against all other test collections.</summary>
[CollectionDefinition("Codex command environment", DisableParallelization = true)]
public sealed class CodexCommandEnvironmentDefinition;

/// <summary>Verifies backend identity selection without launching or mutating a Codex task.</summary>
[Collection("Codex command environment")]
public sealed class CodexAppServerProcessTests : IDisposable
{
    private readonly string? _desktopCommand = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cave backend selection", Guid.NewGuid().ToString("N"));

    /// <summary>Desktop's exact backend remains usable without any standalone CLI on PATH.</summary>
    [Fact]
    public void DesktopBackendDoesNotRequireStandaloneCli()
    {
        var desktop = CreateFile("desktop backend", "codex.exe");
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", desktop);
        Environment.SetEnvironmentVariable("PATH", string.Empty);

        var start = CodexAppServerProcess.CreateStartInfo(null);

        Assert.Equal(desktop, start.FileName);
        Assert.Equal(["app-server"], start.ArgumentList);
        Assert.True(start.RedirectStandardInput);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.True(start.CreateNoWindow);
        Assert.False(start.UseShellExecute);
    }

    /// <summary>A competing standalone installation cannot replace the selected Desktop backend.</summary>
    [Fact]
    public void DesktopBackendWinsOverStandalonePath()
    {
        var desktop = CreateFile("desktop", "codex.exe");
        var standalone = CreateFile("standalone", "codex.cmd");
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", desktop);
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(standalone));

        Assert.Equal(desktop, CodexAppServerProcess.CreateStartInfo(null).FileName);
    }

    /// <summary>Explicit operator configuration wins even when the inherited Desktop path is stale.</summary>
    [Fact]
    public void ConfiguredCommandOverridesDesktopSelection()
    {
        var configured = CreateFile("configured", "codex.exe");
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", Path.Combine(_root, "removed.exe"));

        Assert.Equal(configured, CodexAppServerProcess.CreateStartInfo(configured).FileName);
    }

    /// <summary>Malformed and stale Desktop selections fail visibly rather than selecting PATH.</summary>
    [Theory]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("whitespace")]
    public async Task InvalidDesktopSelectionDoesNotSwitchBackend(string mode)
    {
        var standalone = CreateFile("standalone", "codex.cmd");
        var desktop = mode switch
        {
            "relative" => "codex.exe",
            "missing" => Path.Combine(_root, "missing.exe"),
            _ => "   ",
        };
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", desktop);
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(standalone));

        using var goalProvider = new CodexAppServerGoalProvider(TimeProvider.System);
        var goal = await goalProvider.GetAsync("task-a", CancellationToken.None);
        using var usageProvider = new CodexAppServerUsageProvider(TimeProvider.System);
        var usage = await usageProvider.GetAsync(CancellationToken.None);

        Assert.Equal(CodexGoalSourceStatus.Unavailable, goal.Status);
        Assert.Null(goal.Goal);
        Assert.Contains("CODEX_CLI_PATH", goal.Error);
        Assert.Contains("Cave:CodexCommand", goal.Error);
        Assert.Contains("CODEX_CLI_PATH", usage.Error);
    }

    /// <summary>Standalone Windows launches retain cmd.exe quoting for executable paths containing spaces.</summary>
    [Fact]
    public void StandalonePathRetainsCommandQuoting()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var standalone = CreateFile("standalone cli", "codex.cmd");
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", null);
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(standalone));

        var start = CodexAppServerProcess.CreateStartInfo(null);

        Assert.Equal(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", start.FileName);
        Assert.Equal($"/d /s /c \"\"{standalone}\" app-server\"", start.Arguments);
    }

    /// <summary>Restores inherited process configuration and removes only this fixture's files.</summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", _desktopCommand);
        Environment.SetEnvironmentVariable("PATH", _path);
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private string CreateFile(string directory, string name)
    {
        var path = Path.Combine(_root, directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
