using System.Diagnostics;
using System.Text.Json;

namespace Cave.Infrastructure.Codex;

/// <summary>
/// Owns the canonical Codex App Server process launch and JSON-line write contract.
/// </summary>
internal static class CodexAppServerProcess
{
    /// <summary>Creates a redirected, windowless App Server process start definition.</summary>
    internal static ProcessStartInfo CreateStartInfo(string? configuredCommand)
    {
        var command = string.IsNullOrWhiteSpace(configuredCommand)
            ? ResolveCodexCommand()
            : configuredCommand.Trim();
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows()
            && (command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            // CONSTRAINT: cmd.exe /s requires the outer quote pair in addition to the
            // quoted executable path. Without it, paths containing spaces close before
            // the JSON-RPC initialize response and surface as a misleading EOF.
            startInfo.Arguments = $"/d /s /c \"\"{command}\" app-server\"";
        }
        else
        {
            startInfo.FileName = command;
            startInfo.ArgumentList.Add("app-server");
        }

        return startInfo;
    }

    /// <summary>Writes one JSON-RPC request or notification as a flushed JSON line.</summary>
    internal static async Task WriteAsync(
        Process process,
        object request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveCodexCommand()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "codex";
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.cmd");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return "codex.cmd";
    }
}
