using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Cave.Infrastructure.CodeGraph;

/// <summary>
/// Converts provider and manifest documentation into one readable architecture-node summary.
/// </summary>
internal static partial class DocumentationSummaryExtractor
{
    private static readonly HashSet<string> NonSummaryDocumentationElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "example", "exception", "inheritdoc", "param", "permission", "remarks", "returns", "seealso", "typeparam", "value",
    };

    /// <summary>
    /// Extracts the primary summary from a CodeGraph docstring without folding parameter or remarks text into it.
    /// </summary>
    internal static string? FromDocstring(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var root = XDocument.Parse($"<root>{value}</root>", LoadOptions.PreserveWhitespace).Root;
            var summary = root?.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.Equals("summary", StringComparison.OrdinalIgnoreCase));
            if (summary is not null)
            {
                return Normalize(RenderNodes(summary.Nodes()));
            }

            if (root?.Descendants().Any(element => NonSummaryDocumentationElements.Contains(element.Name.LocalName)) == true)
            {
                return null;
            }

            return Normalize(root?.Value);
        }
        catch (System.Xml.XmlException)
        {
            var summaryMatch = SummaryElementRegex().Match(value);
            if (summaryMatch.Success)
            {
                var withoutMarkup = XmlElementRegex().Replace(summaryMatch.Groups["content"].Value, " ");
                return Normalize(WebUtility.HtmlDecode(withoutMarkup));
            }

            return value.Contains('<')
                ? null
                : Normalize(value);
        }
    }

    /// <summary>
    /// Reads a human-authored project description from an MSBuild manifest when one is present.
    /// </summary>
    internal static string? FromProjectManifest(XDocument manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Descendants()
            .Where(element => element.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase))
            .Select(element => Normalize(element.Value))
            .FirstOrDefault(description => description is not null);
    }

    /// <summary>
    /// Normalizes documentation whitespace while preserving the complete summary for detail views.
    /// </summary>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string RenderNodes(IEnumerable<XNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    builder.Append(text.Value);
                    break;
                case XElement element:
                    RenderElement(element, builder);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void RenderElement(XElement element, StringBuilder builder)
    {
        var elementName = element.Name.LocalName;
        if (elementName.Equals("see", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(ReadableReference(
                element.Attribute("cref")?.Value
                ?? element.Attribute("langword")?.Value
                ?? element.Attribute("href")?.Value));
            return;
        }

        if (elementName.Equals("paramref", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("typeparamref", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(element.Attribute("name")?.Value);
            return;
        }

        var separatesContent = elementName.Equals("para", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("br", StringComparison.OrdinalIgnoreCase)
            || elementName.Equals("item", StringComparison.OrdinalIgnoreCase);
        if (separatesContent)
        {
            builder.Append(' ');
        }

        builder.Append(RenderNodes(element.Nodes()));

        if (separatesContent)
        {
            builder.Append(' ');
        }
    }

    private static string? ReadableReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var value = reference.Trim();
        if (value.Length > 2 && value[1] == ':')
        {
            value = value[2..];
        }

        return value.StartsWith("global::", StringComparison.Ordinal)
            ? value[8..]
            : value;
    }

    [GeneratedRegex(
        @"<summary(?:\s[^>]*)?>(?<content>.*?)</summary\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SummaryElementRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex XmlElementRegex();
}

/// <summary>
/// Supplies a narrow source-trivia fallback when CodeGraph does not emit a C# declaration docstring.
/// </summary>
internal sealed class CSharpDocumentationSummaryReader
{
    private readonly string workspaceRoot;
    private readonly Dictionary<string, string[]?> sourceLines = new(StringComparer.OrdinalIgnoreCase);

    internal CSharpDocumentationSummaryReader(string workspaceRoot)
    {
        this.workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Reads the contiguous <c>///</c> block immediately above a declaration line.
    /// </summary>
    internal string? Read(string relativeFilePath, int declarationStartLine)
    {
        if (declarationStartLine < 2 || !relativeFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sourcePath = ResolveWorkspacePath(relativeFilePath);
        if (sourcePath is null)
        {
            return null;
        }

        if (!sourceLines.TryGetValue(sourcePath, out var lines))
        {
            lines = ReadAllLines(sourcePath);
            sourceLines[sourcePath] = lines;
        }

        if (lines is null || declarationStartLine > lines.Length + 1)
        {
            return null;
        }

        var documentationLines = new Stack<string>();
        for (var index = declarationStartLine - 2; index >= 0; index--)
        {
            var line = lines[index].TrimStart();
            if (!line.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            documentationLines.Push(line[3..]);
        }

        return documentationLines.Count == 0
            ? null
            : DocumentationSummaryExtractor.FromDocstring(string.Join(Environment.NewLine, documentationLines));
    }

    private string? ResolveWorkspacePath(string relativeFilePath)
    {
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(workspaceRoot, relativeFilePath));
            var relativeCandidate = Path.GetRelativePath(workspaceRoot, candidate);
            if (relativeCandidate.Equals("..", StringComparison.Ordinal)
                || relativeCandidate.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relativeCandidate))
            {
                return null;
            }

            return candidate;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string[]? ReadAllLines(string sourcePath)
    {
        try
        {
            return File.ReadAllLines(sourcePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
