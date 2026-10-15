namespace Cave.Application;

/// <summary>
/// Defines the compatibility contract shared by the bundled MCP server and machine viewer.
/// </summary>
public static class CaveRuntimeContract
{
    /// <summary>
    /// Gets the exact machine-viewer protocol version required by this CAVE build.
    /// </summary>
    // CONSTRAINT: protocol 3 requires retained statistics and agent configuration support;
    // an older healthy listener cannot serve the new UI's read contract.
    public const int ViewerProtocolVersion = 3;
}

/// <summary>
/// Describes the compatibility identity exposed by the read-only machine viewer.
/// </summary>
/// <param name="ViewerProtocolVersion">The exact protocol version accepted by the bundled MCP server.</param>
public sealed record CaveRuntimeInfo(int ViewerProtocolVersion);
