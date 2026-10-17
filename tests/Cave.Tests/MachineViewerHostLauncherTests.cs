using System.Net;
using System.Text;
using Cave.Application;
using Cave.Mcp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cave.Tests;

/// <summary>
/// Verifies that the MCP server only reuses a compatible machine viewer.
/// </summary>
public sealed class MachineViewerHostLauncherTests
{
    /// <summary>
    /// Verifies every plugin version resolves the same machine-local listener path.
    /// </summary>
    [Fact]
    public void ViewerExecutableUsesStablePerUserPath()
    {
        var localApplicationData = Path.Combine(Path.GetTempPath(), "cave-local-app-data");

        var executablePath = MachineViewerHostLauncher.ResolveViewerExecutablePath(localApplicationData);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localApplicationData, "CAVE", "viewer", "Cave.Host.exe")),
            executablePath);
    }

    /// <summary>
    /// Verifies exact protocol matches are accepted.
    /// </summary>
    [Fact]
    public async Task CompatibleRuntimeIsReady()
    {
        using var client = CreateClient(
            $"{{\"viewerProtocolVersion\":{CaveRuntimeContract.ViewerProtocolVersion}}}");
        var launcher = new MachineViewerHostLauncher(client, NullLogger<MachineViewerHostLauncher>.Instance);

        Assert.True(await launcher.IsCompatibleAsync(CancellationToken.None));
    }

    /// <summary>
    /// Verifies an older or unrelated process on the viewer port is not silently reused.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task IncompatibleRuntimeIsNotReady(int version)
    {
        using var client = CreateClient($"{{\"viewerProtocolVersion\":{version}}}");
        var launcher = new MachineViewerHostLauncher(client, NullLogger<MachineViewerHostLauncher>.Instance);

        Assert.False(await launcher.IsCompatibleAsync(CancellationToken.None));
    }

    private static HttpClient CreateClient(string json) => new(new StubHandler(json))
    {
        BaseAddress = new Uri("http://127.0.0.1:5098"),
    };

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("?viewerProtocolVersion=3", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
