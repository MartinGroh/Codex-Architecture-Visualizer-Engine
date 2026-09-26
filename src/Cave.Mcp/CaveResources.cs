using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace Cave.Mcp;

/// <summary>
/// Serves the self-contained CAVE MCP App document.
/// </summary>
[McpServerResourceType]
public sealed class CaveResources
{
    /// <summary>
    /// Identifies the bundled CAVE architecture app.
    /// </summary>
    public const string ArchitectureAppUri = "ui://cave/architecture-v1.html";

    /// <summary>
    /// Reads the self-contained architecture app produced by the TypeScript build.
    /// </summary>
    /// <returns>The MCP App HTML document.</returns>
    [McpServerResource(
        UriTemplate = ArchitectureAppUri,
        Name = "cave-architecture-app",
        Title = "CAVE live architecture",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", JsonValue = "{\"prefersBorder\":false}")]
    [Description("Self-contained CAVE live architecture application.")]
    public static string ReadArchitectureApp()
    {
        var appPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
        return File.Exists(appPath)
            ? File.ReadAllText(appPath)
            : throw new FileNotFoundException(
                "The CAVE UI bundle is absent. Run the TypeScript production build before publishing the plugin.",
                appPath);
    }
}
