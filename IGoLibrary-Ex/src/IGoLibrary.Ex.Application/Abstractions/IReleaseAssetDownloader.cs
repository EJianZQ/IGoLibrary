namespace IGoLibrary.Ex.Application.Abstractions;

public interface IReleaseAssetDownloader
{
    Task DownloadAsync(
        ReleaseAssetInfo asset,
        string destinationPath,
        IProgress<ReleaseAssetDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReleaseAssetDownloadPauseSource? pauseSource = null);
}

public sealed record ReleaseAssetDownloadProgress(
    long DownloadedBytes,
    long TotalBytes,
    ReleaseAssetDownloadState State = ReleaseAssetDownloadState.Downloading,
    int RetryAttempt = 0,
    TimeSpan? RetryDelay = null)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)DownloadedBytes / TotalBytes * 100, 0, 100);
}

public enum ReleaseAssetDownloadState
{
    Connecting,
    Downloading,
    Paused,
    Retrying,
    Restarting,
    Verifying
}

public interface IReleaseAssetDownloadPauseSource
{
    bool IsPaused { get; }

    CancellationToken PauseToken { get; }

    ValueTask WaitWhilePausedAsync(CancellationToken cancellationToken = default);
}

public sealed class ReleaseAssetDownloadInterruptedException : IOException
{
    public ReleaseAssetDownloadInterruptedException(
        string message,
        long preservedBytes,
        Exception? innerException = null)
        : base(message, innerException)
    {
        PreservedBytes = Math.Max(0, preservedBytes);
    }

    public long PreservedBytes { get; }

    public bool CanResume => PreservedBytes > 0;
}
