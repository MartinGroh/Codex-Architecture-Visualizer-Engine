using System.Text.Json;
using Cave.Application;
using SemanticWorkspaceModel = Cave.Domain.SemanticWorkspace;

namespace Cave.Infrastructure.SemanticWorkspace;

/// <summary>
/// Atomically persists one schema-versioned semantic workspace below its owning source workspace.
/// </summary>
public sealed class FileSemanticWorkspaceStore : ISemanticWorkspaceStore
{
    /// <summary>Gets the canonical workspace-relative semantic state path.</summary>
    public const string RelativePath = ".cave/semantic-workspace.json";

    private static readonly JsonSerializerOptions JsonOptions = SemanticWorkspaceJson.CreateOptions(writeIndented: true);

    /// <inheritdoc />
    public async Task<SemanticWorkspaceModel?> LoadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var root = CanonicalizeRoot(workspaceRoot);
        var path = GetStatePath(root);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionProperty)
                || !versionProperty.TryGetInt32(out var schemaVersion))
            {
                throw new SemanticWorkspacePersistenceException(
                    $"Semantic workspace '{path}' has no valid integer schemaVersion.");
            }

            if (schemaVersion != SemanticWorkspaceModel.CurrentSchemaVersion)
            {
                throw new UnsupportedSemanticWorkspaceSchemaException(
                    schemaVersion,
                    SemanticWorkspaceModel.CurrentSchemaVersion);
            }

            return document.RootElement.Deserialize<SemanticWorkspaceModel>(JsonOptions)
                ?? throw new SemanticWorkspacePersistenceException(
                    $"Semantic workspace '{path}' contains no aggregate.");
        }
        catch (SemanticWorkspacePersistenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{path}' is invalid JSON: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{path}' could not be read: {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{path}' is not readable: {exception.Message}",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string workspaceRoot,
        SemanticWorkspaceModel workspace,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.SchemaVersion != SemanticWorkspaceModel.CurrentSchemaVersion)
        {
            throw new UnsupportedSemanticWorkspaceSchemaException(
                workspace.SchemaVersion,
                SemanticWorkspaceModel.CurrentSchemaVersion);
        }

        var root = CanonicalizeRoot(workspaceRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace root '{root}' does not exist.");
        }

        var finalPath = GetStatePath(root);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory, ".semantic-workspace.lock");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using var writeLock = await AcquireWriteLockAsync(lockPath, cancellationToken).ConfigureAwait(false);
            var actualRevision = await ReadCurrentRevisionAsync(finalPath, cancellationToken).ConfigureAwait(false);
            if (actualRevision != expectedRevision)
            {
                throw new SemanticWorkspaceConcurrencyException(expectedRevision, actualRevision);
            }

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    workspace,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        catch (JsonException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{finalPath}' could not be serialized: {exception.Message}",
                exception);
        }
        catch (SemanticWorkspaceConcurrencyException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{finalPath}' could not be atomically replaced: {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{finalPath}' is not writable: {exception.Message}",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // The committed file is authoritative; a locked orphaned temp file is never read as state.
            }
            catch (UnauthorizedAccessException)
            {
                // The committed file is authoritative; a protected orphaned temp file is never read as state.
            }
        }
    }

    private static string GetStatePath(string canonicalRoot) =>
        Path.Combine(canonicalRoot, ".cave", "semantic-workspace.json");

    private static async Task<FileStream> AcquireWriteLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<long> ReadCurrentRevisionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("revision", out var revisionProperty)
            || !revisionProperty.TryGetInt64(out var revision))
        {
            throw new SemanticWorkspacePersistenceException(
                $"Semantic workspace '{path}' has no valid integer revision.");
        }

        return revision;
    }

    private static string CanonicalizeRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
        {
            throw new ArgumentException("CAVE requires an absolute semantic-workspace root.", nameof(workspaceRoot));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }
}
