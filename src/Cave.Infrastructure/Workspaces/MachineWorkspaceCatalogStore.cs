using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cave.Application;

namespace Cave.Infrastructure.Workspaces;

/// <summary>
/// Stores one atomic registration file per workspace in the current user's machine-local CAVE directory.
/// </summary>
/// <param name="catalogRoot">The absolute catalog directory.</param>
/// <param name="timeProvider">The authoritative registration clock.</param>
public sealed class MachineWorkspaceCatalogStore(
    string catalogRoot,
    TimeProvider timeProvider) : IWorkspaceCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _catalogRoot = CanonicalizeCatalogRoot(catalogRoot);

    /// <summary>
    /// Gets the default per-user catalog directory for the current machine.
    /// </summary>
    /// <returns>The absolute machine-local catalog path.</returns>
    public static string GetDefaultCatalogRoot()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "CAVE cannot locate the current user's LocalApplicationData directory.");
        }

        return Path.Combine(localApplicationData, "CAVE", "workspaces");
    }

    /// <summary>
    /// Creates the stable opaque identity shared by the .NET hosts and the Codex hook adapter.
    /// </summary>
    /// <param name="workspaceRoot">The absolute workspace root.</param>
    /// <returns>A lowercase 96-bit SHA-256 prefix of the platform-normalized canonical path.</returns>
    public static string CreateWorkspaceId(string workspaceRoot)
    {
        var canonicalRoot = CanonicalizeWorkspaceRoot(workspaceRoot);
        var identityPath = OperatingSystem.IsWindows()
            ? canonicalRoot.ToLowerInvariant()
            : canonicalRoot;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identityPath));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    /// <inheritdoc />
    public async Task<WorkspaceCatalogEntry> RegisterAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var canonicalRoot = CanonicalizeWorkspaceRoot(workspaceRoot);
        if (!Directory.Exists(canonicalRoot))
        {
            throw new DirectoryNotFoundException($"Workspace root '{canonicalRoot}' does not exist.");
        }

        var workspaceId = CreateWorkspaceId(canonicalRoot);
        var record = new WorkspaceCatalogRecord(
            SchemaVersion: 1,
            workspaceId,
            canonicalRoot,
            timeProvider.GetUtcNow());
        Directory.CreateDirectory(_catalogRoot);
        var finalPath = Path.Combine(_catalogRoot, $"{workspaceId}.json");
        var temporaryPath = Path.Combine(_catalogRoot, $".{workspaceId}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new WorkspaceCatalogEntry(workspaceId, canonicalRoot, record.LastSeenAtUtc);
    }

    /// <inheritdoc />
    public async Task<WorkspaceCatalogReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_catalogRoot))
        {
            return new WorkspaceCatalogReadResult([], []);
        }

        var entries = new List<WorkspaceCatalogEntry>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(_catalogRoot, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var record = await JsonSerializer.DeserializeAsync<WorkspaceCatalogRecord>(
                    stream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (record is null || record.SchemaVersion != 1)
                {
                    errors.Add($"{Path.GetFileName(path)} has an unsupported workspace catalog schema.");
                    continue;
                }

                var canonicalRoot = CanonicalizeWorkspaceRoot(record.WorkspaceRoot);
                var expectedId = CreateWorkspaceId(canonicalRoot);
                if (!expectedId.Equals(record.WorkspaceId, StringComparison.Ordinal)
                    || !Path.GetFileNameWithoutExtension(path).Equals(expectedId, StringComparison.Ordinal))
                {
                    errors.Add($"{Path.GetFileName(path)} has an invalid workspace identity.");
                    continue;
                }

                entries.Add(new WorkspaceCatalogEntry(expectedId, canonicalRoot, record.LastSeenAtUtc));
            }
            catch (JsonException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
            catch (IOException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
            catch (ArgumentException exception)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        return new WorkspaceCatalogReadResult(
            entries
                .GroupBy(entry => entry.WorkspaceId, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(entry => entry.LastSeenAtUtc).First())
                .ToArray(),
            errors);
    }

    private static string CanonicalizeCatalogRoot(string catalogRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogRoot);
        if (!Path.IsPathFullyQualified(catalogRoot))
        {
            throw new ArgumentException("CAVE requires an absolute workspace catalog path.", nameof(catalogRoot));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(catalogRoot));
    }

    private static string CanonicalizeWorkspaceRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException("CAVE requires an absolute workspace path.", nameof(workspaceRoot));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }

    private sealed record WorkspaceCatalogRecord(
        int SchemaVersion,
        string WorkspaceId,
        string WorkspaceRoot,
        DateTimeOffset LastSeenAtUtc);
}
