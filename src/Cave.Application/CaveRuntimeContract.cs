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

    /// <summary>
    /// Gets the retained protocol used by already-running MCP servers before explicit negotiation.
    /// </summary>
    // CONSTRAINT: protocol 3 adds statistics without removing the protocol-2 HTTP surface.
    // Old task-owned supervisors cannot be updated in place and must still reuse this viewer.
    public const int LegacyViewerProtocolVersion = 2;
}

/// <summary>
/// Describes the compatibility identity exposed by the read-only machine viewer.
/// </summary>
/// <param name="ViewerProtocolVersion">The supported protocol selected for this compatibility probe.</param>
/// <param name="LatestViewerProtocolVersion">The newest implemented protocol, when advertised by the viewer.</param>
public sealed record CaveRuntimeInfo(int ViewerProtocolVersion, int? LatestViewerProtocolVersion = null);
