using IGoLibrary.Ex.Application.Abstractions;
using Avalonia.Threading;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class WindowsUpdateProgressDialogService(
    AppWindowService appWindowService,
    IUpdateInstallGuard installGuard,
    IWindowsPortableUpdateService updateService) : IWindowsUpdateProgressDialogService
{
    public async Task<WindowsPortableUpdateResult> ShowAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        var blockingTasks = installGuard.GetBlockingTaskNames();
        if (blockingTasks.Count > 0)
        {
            return new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Blocked,
                $"以下任务仍在运行，请先停止：{string.Join("、", blockingTasks)}");
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => ShowAsync(release, cancellationToken));
        }

        if (appWindowService.MainWindow is not { } owner)
        {
            return new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Failed,
                "主窗口尚未就绪，无法开始自动更新");
        }

        var dialog = new WindowsUpdateProgressWindow(
            (progress, token) => updateService.DownloadAndInstallAsync(release, progress, token));
        return await dialog.ShowDialog<WindowsPortableUpdateResult>(owner);
    }
}
