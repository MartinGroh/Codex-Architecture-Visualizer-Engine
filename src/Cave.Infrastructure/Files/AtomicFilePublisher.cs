namespace Cave.Infrastructure.Files;

/// <summary>Owns atomic first-publication and replacement of same-directory immutable record snapshots.</summary>
internal static class AtomicFilePublisher
{
    /// <summary>Publishes a closed temporary file, tolerating bounded Windows reader contention without deleting prior data.</summary>
    internal static async Task PublishAsync(string temporaryPath, string destinationPath, CancellationToken cancellationToken)
    {
        // CONSTRAINT: Windows File.Move(overwrite: true) can deny an open destination even
        // with delete sharing (dotnet/runtime#114230). Wait for bounded readers to release
        // their handles, then retry the SAME atomic operation. Never delete/copy/replace
        // through a missing-path gap or hide a persistent permission failure.
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && attempt < 40
                && exception is IOException or UnauthorizedAccessException
                && (exception.HResult & 0xffff) is 5 or 32 or 33)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
