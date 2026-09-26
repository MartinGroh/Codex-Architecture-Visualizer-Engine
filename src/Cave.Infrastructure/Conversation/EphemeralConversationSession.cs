using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cave.Infrastructure.Conversation;

/// <summary>
/// Marks CAVE-owned ephemeral Codex forks so hook and projection paths exclude them from shared state.
/// </summary>
internal static class EphemeralConversationSession
{
    internal const string MarkerDirectory = ".cave/conversation/ephemeral";
    private const int MaxMarkers = 4096;

    public static async Task MarkAsync(
        string workspaceRoot,
        string sessionId,
        string sourceSessionId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSessionId);
        var directory = Path.Combine(Path.GetFullPath(workspaceRoot), ".cave", "conversation", "ephemeral");
        Directory.CreateDirectory(directory);
        var finalPath = GetMarkerPath(workspaceRoot, sessionId);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(new Marker(
                SchemaVersion: 1,
                SessionId: sessionId.Trim(),
                SourceSessionId: sourceSessionId.Trim(),
                CreatedAtUtc: createdAtUtc));
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, finalPath, overwrite: true);
            Prune(directory);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static bool IsMarked(string workspaceRoot, string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId)
        && File.Exists(GetMarkerPath(workspaceRoot, sessionId));

    private static string GetMarkerPath(string workspaceRoot, string sessionId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Trim())))
            .ToLowerInvariant();
        return Path.Combine(
            Path.GetFullPath(workspaceRoot),
            ".cave",
            "conversation",
            "ephemeral",
            $"{hash}.json");
    }

    private static void Prune(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(MaxMarkers))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record Marker(
        int SchemaVersion,
        string SessionId,
        string SourceSessionId,
        DateTimeOffset CreatedAtUtc);
}
