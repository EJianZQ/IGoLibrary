namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredInstallService
{
    CloudflaredAssetDescriptor Asset { get; }

    bool TryPause();

    bool TryResume();

    Task InstallAsync(
        IProgress<CloudflaredInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal sealed record CloudflaredInstallProgress(
    CloudflaredInstallStage Stage,
    string Status,
    long CompletedBytes = 0,
    long TotalBytes = 0,
    bool CanCancel = true,
    bool CanPause = false,
    bool CanResume = false)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)CompletedBytes / TotalBytes * 100, 0, 100);
}

internal enum CloudflaredInstallStage
{
    Connecting,
    Downloading,
    Paused,
    Retrying,
    Verifying,
    Extracting,
    Installing,
    Completed
}
