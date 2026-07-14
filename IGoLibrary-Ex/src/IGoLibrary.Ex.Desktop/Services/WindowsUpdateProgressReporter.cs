namespace IGoLibrary.Ex.Desktop.Services;

internal static class WindowsUpdateProgressReporter
{
    public static void Report(
        IProgress<WindowsUpdateProgress> progress,
        WindowsUpdateStage stage,
        string status,
        long completedBytes = 0,
        long totalBytes = 0,
        WindowsUpdateTransferState transferState = WindowsUpdateTransferState.None,
        WindowsUpdateAvailableActions actions = WindowsUpdateAvailableActions.Cancel)
    {
        progress.Report(new WindowsUpdateProgress(
            stage,
            completedBytes,
            totalBytes,
            status,
            transferState,
            actions));
    }
}
