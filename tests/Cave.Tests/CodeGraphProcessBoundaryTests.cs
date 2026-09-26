using Cave.Infrastructure.CodeGraph;

namespace Cave.Tests;

/// <summary>
/// Verifies that the CodeGraph process adapter remains isolated from the MCP stdio transport.
/// </summary>
public sealed class CodeGraphProcessBoundaryTests
{
    /// <summary>
    /// Verifies that the CodeGraph helper receives private redirected standard streams.
    /// </summary>
    [Fact]
    public void SnapshotProcessUsesClosedPrivateStandardInput()
    {
        var startInfo = CodeGraphSemanticIndex.CreateSnapshotStartInfo(
            "node.exe",
            "codegraph-snapshot.mjs",
            Path.GetTempPath());

        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
    }
}
