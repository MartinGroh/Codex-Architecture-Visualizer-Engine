using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Cave.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cave.Mcp;

/// <summary>
/// Ensures the installed per-user CAVE HTTP viewer is available before the MCP server begins serving tools.
/// </summary>
/// <param name="httpClient">The bounded loopback health client.</param>
/// <param name="logger">The startup diagnostics sink.</param>
public sealed class MachineViewerHostLauncher(
    HttpClient httpClient,
    ILogger<MachineViewerHostLauncher> logger)
{
    private static readonly Uri RuntimeInfoUri = new("http://127.0.0.1:5098/api/runtime");

    /// <summary>
    /// Reuses a compatible running viewer or starts the stable per-user viewer installation.
    /// </summary>
    /// <param name="cancellationToken">Signals that bounded viewer startup should stop.</param>
    public async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (await IsCompatibleAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var executablePath = ResolveViewerExecutablePath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The stable CAVE machine viewer is missing. Run the canonical CAVE plugin installer.",
                executablePath);
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("The CAVE machine viewer process could not be started.");
        ViewerHostLog.Started(logger, process.Id);

        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsCompatibleAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The CAVE machine viewer exited during startup with code {process.ExitCode}. " +
                    "Check whether another process owns port 5098.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The CAVE machine viewer did not become ready within 10 seconds.");
    }

    /// <summary>
    /// Resolves the stable machine-local viewer executable shared by every installed plugin version.
    /// </summary>
    /// <param name="localApplicationDataRoot">The current user's local application-data root.</param>
    /// <returns>The absolute path of the canonical CAVE network listener.</returns>
    /// <exception cref="ArgumentException">Thrown when the application-data root is empty.</exception>
    public static string ResolveViewerExecutablePath(string localApplicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataRoot);
        return Path.Combine(
            Path.GetFullPath(localApplicationDataRoot),
            "CAVE",
            "viewer",
            "Cave.Host.exe");
    }

    /// <summary>
    /// Checks whether the process on the machine-viewer endpoint implements this build's exact protocol.
    /// </summary>
    /// <param name="cancellationToken">Signals that the bounded compatibility probe should stop.</param>
    /// <returns><see langword="true"/> only when the viewer protocol is an exact match.</returns>
    public async Task<bool> IsCompatibleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(RuntimeInfoUri, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var runtime = await response.Content.ReadFromJsonAsync<CaveRuntimeInfo>(cancellationToken)
                .ConfigureAwait(false);
            return runtime?.ViewerProtocolVersion == CaveRuntimeContract.ViewerProtocolVersion;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// Supervises the independent machine viewer without making embedded MCP tools depend on HTTP availability.
/// </summary>
/// <param name="launcher">The bounded viewer process adapter.</param>
/// <param name="logger">The recovery diagnostics sink.</param>
public sealed class MachineViewerHostSupervisor(
    MachineViewerHostLauncher launcher,
    ILogger<MachineViewerHostSupervisor> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await launcher.EnsureRunningAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ViewerHostLog.RecoveryFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
}

internal static partial class ViewerHostLog
{
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Started the CAVE machine viewer process {ProcessId}.")]
    public static partial void Started(ILogger logger, int processId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "The CAVE machine viewer is unavailable; background recovery will continue.")]
    public static partial void RecoveryFailed(ILogger logger, Exception exception);
}
