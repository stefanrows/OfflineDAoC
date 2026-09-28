using System;
using System.IO;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// Shared retry helper for periodic status/snapshot publishers that replace a
/// small file with <see cref="File.Move(string, string, bool)"/>. On Windows
/// that call fails with <see cref="IOException"/> or
/// <see cref="UnauthorizedAccessException"/> for as long as anything else
/// (an antivirus scan, a file indexer, or a reader opened without delete
/// sharing) holds the destination file open without
/// <see cref="FileShare.Delete"/>. Such holds are normally a few
/// milliseconds, so a bounded retry with a short backoff recovers the
/// publish instead of dropping the whole cycle and logging a warning.
/// </summary>
internal static class AtomicFilePublish
{
    public const int DefaultMaxAttempts = 6;
    public const int DefaultRetryDelayMilliseconds = 25;

    /// <summary>
    /// Moves <paramref name="sourcePath"/> onto <paramref name="destinationPath"/>,
    /// overwriting it, retrying past a transient lock on the destination.
    /// Throws the underlying exception if every attempt fails.
    /// </summary>
    /// <param name="onAttemptFailed">
    /// Invoked synchronously, on the caller's thread, right after a retryable
    /// attempt fails and before the backoff delay for that attempt. Production
    /// callers leave this null; tests use it to release a simulated lock
    /// deterministically instead of racing the backoff delay with a timer.
    /// </param>
    public static void MoveWithRetry(string sourcePath, string destinationPath,
        int maxAttempts = DefaultMaxAttempts, int retryDelayMilliseconds = DefaultRetryDelayMilliseconds,
        Action<int> onAttemptFailed = null)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath, true);
                return;
            }
            catch (Exception exception) when (
                (exception is IOException || exception is UnauthorizedAccessException) &&
                attempt < maxAttempts)
            {
                onAttemptFailed?.Invoke(attempt);
                Thread.Sleep(retryDelayMilliseconds * attempt);
            }
        }
    }
}
