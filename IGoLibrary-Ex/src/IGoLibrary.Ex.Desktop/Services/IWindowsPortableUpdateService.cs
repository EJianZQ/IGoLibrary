using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IWindowsPortableUpdateService
{
    Task<WindowsPortableUpdateResult> DownloadAndInstallAsync(
        ReleaseUpdateInfo release,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken = default);
}

public enum WindowsUpdateStage
{
    Checking,
    Downloading,
    Verifying,
    Extracting,
    WaitingForExit,
    Installing,
    Validating,
    RollingBack
}

public sealed record WindowsUpdateProgress(
    WindowsUpdateStage Stage,
    long CompletedBytes,
    long TotalBytes,
    string Status,
    bool CanCancel = true)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)CompletedBytes / TotalBytes * 100, 0, 100);
}

public enum WindowsPortableUpdateOutcome
{
    ExitRequested,
    Blocked,
    Canceled,
    Failed
}

public sealed record WindowsPortableUpdateResult(
    WindowsPortableUpdateOutcome Outcome,
    string Message)
{
    public bool ExitRequested => Outcome == WindowsPortableUpdateOutcome.ExitRequested;
}
