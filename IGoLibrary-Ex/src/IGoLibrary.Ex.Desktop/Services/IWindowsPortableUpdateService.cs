using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IWindowsPortableUpdateService
{
    IWindowsPortableUpdateOperation CreateOperation(ReleaseUpdateInfo release);
}

public interface IWindowsPortableUpdateOperation : IDisposable
{
    Task<WindowsPortableUpdateResult> RunAsync(
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken = default);

    bool TryPause();

    bool TryResume();
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
    WindowsUpdateTransferState TransferState = WindowsUpdateTransferState.None,
    WindowsUpdateAvailableActions AvailableActions = WindowsUpdateAvailableActions.Cancel)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp((double)CompletedBytes / TotalBytes * 100, 0, 100);

    public bool CanPause => AvailableActions.HasFlag(WindowsUpdateAvailableActions.Pause);

    public bool CanResume => AvailableActions.HasFlag(WindowsUpdateAvailableActions.Resume);

    public bool CanCancel => AvailableActions.HasFlag(WindowsUpdateAvailableActions.Cancel);
}

public enum WindowsUpdateTransferState
{
    None,
    Connecting,
    Downloading,
    Paused,
    Retrying,
    AwaitingManualResume,
    Verifying
}

[Flags]
public enum WindowsUpdateAvailableActions
{
    None = 0,
    Pause = 1,
    Resume = 2,
    Cancel = 4
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
