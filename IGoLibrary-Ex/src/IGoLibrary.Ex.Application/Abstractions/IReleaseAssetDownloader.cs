namespace IGoLibrary.Ex.Application.Abstractions;

public interface IReleaseAssetDownloader
{
    Task DownloadAsync(
        ReleaseAssetInfo asset,
        string destinationPath,
        IProgress<ReleaseAssetDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ReleaseAssetDownloadProgress(
    long DownloadedBytes,
    long TotalBytes)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)DownloadedBytes / TotalBytes * 100, 0, 100);
}
