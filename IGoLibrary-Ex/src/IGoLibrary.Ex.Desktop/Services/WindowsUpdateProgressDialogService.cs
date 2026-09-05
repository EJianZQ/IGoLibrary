using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Updates;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class WindowsUpdateProgressDialogService(
    AppWindowService appWindowService,
    IUpdateInstallGuard installGuard,
    IWindowsPortableUpdateService updateService,
    IAppVersionProvider appVersionProvider,
    ILogger<WindowsUpdateProgressDialogService> logger) : IWindowsUpdateProgressDialogService
{
    public async Task<WindowsPortableUpdateResult> ShowAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = appVersionProvider.CurrentVersion;
        var eligibility = release.AutomaticUpdatePolicy.Evaluate(currentVersion);
        if (eligibility != AutomaticUpdateEligibility.Supported)
        {
            var message = AutomaticUpdateCompatibilityMessages.BuildBlockedMessage(
                release.AutomaticUpdatePolicy,
                currentVersion);
            logger.LogWarning(
                "更新进度弹窗拒绝了不兼容的自动更新请求。当前版本={CurrentVersion}，目标版本={TargetVersion}，策略={AutomaticUpdatePolicy}，最低自动更新版本={MinimumAutomaticUpdateVersion}，自动更新资格={AutomaticUpdateEligibility}。",
                currentVersion,
                release.Version,
                release.AutomaticUpdatePolicy.Kind,
                release.AutomaticUpdatePolicy.MinimumVersion,
                eligibility);
            return new WindowsPortableUpdateResult(
                WindowsPortableUpdateOutcome.Blocked,
                message);
        }

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

        using var operation = updateService.CreateOperation(release);
        var dialog = new WindowsUpdateProgressWindow(operation, cancellationToken);
        return await dialog.ShowDialog<WindowsPortableUpdateResult>(owner);
    }
}
