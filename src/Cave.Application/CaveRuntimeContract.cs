namespace Cave.Application;

/// <summary>
/// Defines the compatibility contract shared by the bundled MCP server and machine viewer.
/// </summary>
public static class CaveRuntimeContract
{
    /// <summary>
    /// Gets the exact machine-viewer protocol version required by this CAVE build.
    /// </summary>
    public const int ViewerProtocolVersion = 2;
}

/// <summary>
/// Describes the compatibility identity exposed by the read-only machine viewer.
/// </summary>
/// <param name="ViewerProtocolVersion">The exact protocol version accepted by the bundled MCP server.</param>
public sealed record CaveRuntimeInfo(int ViewerProtocolVersion);
