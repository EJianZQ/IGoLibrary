using System.Runtime.InteropServices;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WindowsPortableUpdateOperation(
    ReleaseUpdateInfo release,
    IUpdateInstallGuard installGuard,
    IAppVersionProvider appVersionProvider,
    AppWindowService appWindowService,
    IWindowsUpdatePackagePreparationService packagePreparationService,
    IWindowsUpdateHandoffService handoffService,
    WindowsUpdateWorkspaceManager workspaceManager,
    ILogger<WindowsPortableUpdateOperation> logger) : IWindowsPortableUpdateOperation
{
    private readonly ReleaseAssetDownloadPauseController _transferController = new();
    private int _runStarted;
    private int _disposed;
    private int _availableActions;

    public bool TryPause()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !HasAvailableAction(WindowsUpdateAvailableActions.Pause))
        {
            return false;
        }

        try
        {
            return _transferController.TryPause();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public bool TryResume()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !HasAvailableAction(WindowsUpdateAvailableActions.Resume))
        {
            return false;
        }

        try
        {
            return _transferController.TryResume();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public async Task<WindowsPortableUpdateResult> RunAsync(
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _runStarted, 1) != 0)
        {
            throw new InvalidOperationException("同一个更新操作只能运行一次");
        }

        var trackedProgress = new TrackingProgress(this, progress);
        var currentVersion = appVersionProvider.CurrentVersionText;
        var targetVersion = release.Version.ToString();
        WindowsUpdateWorkspace? workspace = null;
        var preserveWorkspace = false;
        var restoreVerifiedCache = false;
        var operationStage = "验证运行平台";

        try
        {
            if (!OperatingSystem.IsWindows() ||
                RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                logger.LogWarning(
                    "已拒绝自动更新：当前平台不受支持。目标版本={TargetVersion}，操作系统={OperatingSystem}，架构={Architecture}。",
                    targetVersion,
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.OSArchitecture);
                return Failed("自动更新仅支持 Windows 10/11 x64 绿色版");
            }

            if (release.WindowsX64Package is not { } asset)
            {
                logger.LogWarning(
                    "已拒绝自动更新：Release 没有可安装的 Windows x64 绿色版资源。当前版本={CurrentVersion}，目标版本={TargetVersion}。",
                    currentVersion,
                    targetVersion);
                return Failed("此版本没有可自动安装的 Windows x64 更新包，请前往 GitHub 下载");
            }

            logger.LogInformation(
                "开始 Windows 绿色版自动更新。当前版本={CurrentVersion}，目标版本={TargetVersion}，资源={AssetName}，大小={PackageSize}字节，摘要={PackageDigest}。",
                currentVersion,
                targetVersion,
                asset.Name,
                asset.Size,
                asset.Digest);

            WindowsUpdateProgressReporter.Report(
                trackedProgress,
                WindowsUpdateStage.Checking,
                "正在检查运行中的任务…",
                actions: WindowsUpdateAvailableActions.Cancel);
            var blockingTasks = installGuard.GetBlockingTaskNames();
            if (blockingTasks.Count > 0)
            {
                logger.LogWarning(
                    "自动更新被运行中的任务阻止。任务={BlockingTasks}，目标版本={TargetVersion}。",
                    string.Join("、", blockingTasks),
                    targetVersion);
                return Blocked(blockingTasks);
            }

            operationStage = "验证当前绿色版安装包";
            var installationDirectory = packagePreparationService.ValidateInstallationDirectory(
                currentVersion);
            logger.LogInformation(
                "当前绿色版安装包验证通过。版本={CurrentVersion}，目录={InstallationDirectory}。",
                currentVersion,
                installationDirectory);

            operationStage = "查找已验签更新缓存";
            workspace = await workspaceManager.TryFindVerifiedAsync(
                            asset,
                            targetVersion,
                            cancellationToken)
                        ?? workspaceManager.Create();
            if (!workspace.IsVerifiedCache)
            {
                logger.LogInformation(
                    "已创建更新下载工作区。事务={TransactionId}。",
                    workspace.TransactionId);
            }

            operationStage = "准备更新包";
            var package = await packagePreparationService.PrepareAsync(
                workspace,
                installationDirectory,
                asset,
                currentVersion,
                targetVersion,
                _transferController,
                trackedProgress,
                cancellationToken);

            WindowsUpdateProgressReporter.Report(
                trackedProgress,
                WindowsUpdateStage.Checking,
                "正在再次检查运行中的任务…",
                actions: WindowsUpdateAvailableActions.Cancel);
            blockingTasks = installGuard.GetBlockingTaskNames();
            if (blockingTasks.Count > 0)
            {
                preserveWorkspace = true;
                logger.LogWarning(
                    "更新包已完整验签，但安装被运行中的任务阻止。任务={BlockingTasks}，事务={TransactionId}，目标版本={TargetVersion}。",
                    string.Join("、", blockingTasks),
                    workspace.TransactionId,
                    targetVersion);
                return Blocked(
                    blockingTasks,
                    "更新包已安全缓存；停止任务后可重新检查更新并安装");
            }

            operationStage = "交接独立更新组件";
            var handoff = await handoffService.ExecuteAsync(
                package,
                trackedProgress,
                cancellationToken);
            preserveWorkspace = handoff.ReadyForExit || !handoff.CanRestoreVerifiedCache;
            restoreVerifiedCache = !handoff.ReadyForExit &&
                                   handoff.CanRestoreVerifiedCache &&
                                   workspace.IsVerifiedCache;
            if (handoff.ReadyForExit)
            {
                logger.LogInformation(
                    "更新组件已就绪，正在退出应用并交接安装。事务={TransactionId}，目标版本={TargetVersion}。",
                    workspace.TransactionId,
                    targetVersion);
                appWindowService.QuitApplication();
            }
            else if (handoff.Outcome == WindowsPortableUpdateOutcome.Failed)
            {
                logger.LogError(
                    handoff.Failure,
                    "Windows 绿色版自动更新交接未完成。事务={TransactionId}，结果={Outcome}。",
                    workspace.TransactionId,
                    handoff.Outcome);
            }

            return new WindowsPortableUpdateResult(handoff.Outcome, handoff.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "用户取消了 Windows 绿色版自动更新。阶段={Stage}，目标版本={TargetVersion}，事务={TransactionId}。",
                operationStage,
                targetVersion,
                workspace?.TransactionId ?? "未创建");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Windows 绿色版自动更新失败。阶段={Stage}，当前版本={CurrentVersion}，目标版本={TargetVersion}，事务={TransactionId}。",
                operationStage,
                currentVersion,
                targetVersion,
                workspace?.TransactionId ?? "未创建");
            return Failed(ToUserMessage(exception));
        }
        finally
        {
            Volatile.Write(ref _availableActions, (int)WindowsUpdateAvailableActions.None);
            if (restoreVerifiedCache && workspace is not null)
            {
                preserveWorkspace = true;
                workspaceManager.TryRestoreVerifiedCache(
                    workspace,
                    "独立更新组件未完成安装交接");
            }

            if (workspace is not null && ShouldDeleteWorkspace(preserveWorkspace, workspace))
            {
                workspaceManager.TryDelete(workspace, "更新操作未交接或已取消");
            }
            else if (!preserveWorkspace && workspace?.IsVerifiedCache == true)
            {
                logger.LogInformation(
                    "更新操作未完成安装交接，已保留完整验签缓存。事务={TransactionId}，目标版本={TargetVersion}。",
                    workspace.TransactionId,
                    targetVersion);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _availableActions, (int)WindowsUpdateAvailableActions.None);
        _transferController.Dispose();
    }

    internal static bool ShouldDeleteWorkspace(
        bool preserveWorkspace,
        WindowsUpdateWorkspace workspace)
    {
        return !preserveWorkspace && !workspace.IsVerifiedCache;
    }

    private bool HasAvailableAction(WindowsUpdateAvailableActions action)
    {
        var available = (WindowsUpdateAvailableActions)Volatile.Read(ref _availableActions);
        return available.HasFlag(action);
    }

    private static WindowsPortableUpdateResult Blocked(
        IReadOnlyList<string> tasks,
        string? suffix = null)
    {
        var message = $"以下任务仍在运行，请先停止：{string.Join("、", tasks)}";
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            message += $"。{suffix}";
        }

        return new WindowsPortableUpdateResult(WindowsPortableUpdateOutcome.Blocked, message);
    }

    private static WindowsPortableUpdateResult Failed(string message)
    {
        return new WindowsPortableUpdateResult(WindowsPortableUpdateOutcome.Failed, message);
    }

    private static string ToUserMessage(Exception exception)
    {
        return exception switch
        {
            TimeoutException => exception.Message,
            UnauthorizedAccessException => "没有读取或写入更新文件所需的权限",
            InvalidDataException => $"更新包校验失败：{exception.Message}",
            IOException => $"更新文件处理失败：{exception.Message}",
            _ => $"自动更新失败：{exception.Message}"
        };
    }

    private sealed class TrackingProgress(
        WindowsPortableUpdateOperation owner,
        IProgress<WindowsUpdateProgress> inner) : IProgress<WindowsUpdateProgress>
    {
        public void Report(WindowsUpdateProgress value)
        {
            Volatile.Write(ref owner._availableActions, (int)value.AvailableActions);
            inner.Report(value);
        }
    }
}
