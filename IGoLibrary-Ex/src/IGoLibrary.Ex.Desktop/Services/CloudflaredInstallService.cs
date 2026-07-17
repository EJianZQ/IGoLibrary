using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class CloudflaredInstallService(
    CloudflaredAssetCatalog catalog,
    ICloudflaredPathProvider paths,
    ICloudflaredToolLocator locator,
    ICloudflaredDownloadWorkspace workspace,
    ICloudflaredManagedInstaller managedInstaller,
    IReleaseAssetDownloader downloader,
    IActivityLogService activityLogService,
    ILogger<CloudflaredInstallService> logger) : ICloudflaredInstallService, IHostedService, IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ReleaseAssetDownloadPauseController _transferController = new();
    private int _transferActions;
    private bool _started;
    private bool _disposed;
    private Exception? _workspaceInitializationError;

    public CloudflaredAssetDescriptor Asset => catalog.Current;

    public bool TryPause()
    {
        if (_disposed || !HasTransferAction(CloudflaredTransferActions.Pause))
        {
            return false;
        }

        try
        {
            if (!_transferController.TryPause())
            {
                return false;
            }

            logger.LogInformation("用户请求暂停 cloudflared 下载。");
            activityLogService.Write(LogEntryKind.Info, "Cloudflared", "正在暂停 cloudflared 下载");
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public bool TryResume()
    {
        if (_disposed || !HasTransferAction(CloudflaredTransferActions.Resume))
        {
            return false;
        }

        try
        {
            if (!_transferController.TryResume())
            {
                return false;
            }

            logger.LogInformation(
                "用户请求继续 cloudflared 下载。续传偏移={ResumeOffset}。",
                workspace.GetPreservedBytes(Asset));
            activityLogService.Write(LogEntryKind.Info, "Cloudflared", "正在继续 cloudflared 下载");
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_started)
        {
            return;
        }

        try
        {
            workspace.Initialize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _workspaceInitializationError = exception;
            logger.LogError(exception, "初始化 cloudflared 下载工作区失败；应用将继续启动，按需下载暂不可用。");
            activityLogService.Write(
                LogEntryKind.Warning,
                "Cloudflared",
                "cloudflared 下载工作区初始化失败，按需下载暂不可用");
        }

        try
        {
            await CleanupObsoleteManagedInstallsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "维护 cloudflared 用户级安装目录失败，将在下次启动重试。");
        }

        _started = true;
        if (_workspaceInitializationError is null)
        {
            logger.LogInformation(
                "cloudflared 下载工作区已初始化。版本={Version}，运行时={RuntimeIdentifier}。",
                Asset.Version,
                Asset.RuntimeIdentifier);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        _shutdown.Cancel();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            workspace.Cleanup("应用退出");
            _started = false;
        }
        finally
        {
            Volatile.Write(ref _transferActions, (int)CloudflaredTransferActions.None);
            if (_transferController.IsPaused)
            {
                _transferController.TryResume();
            }

            _operationGate.Release();
        }
    }

    public async Task InstallAsync(
        IProgress<CloudflaredInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStarted();
        if (_workspaceInitializationError is not null)
        {
            throw new IOException(
                "无法初始化 cloudflared 下载工作区，请检查临时目录权限后重试",
                _workspaceInitializationError);
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await _operationGate.WaitAsync(linkedCancellation.Token);
        try
        {
            var availability = await locator.FindAsync(linkedCancellation.Token);
            if (availability.IsAvailable)
            {
                logger.LogInformation("cloudflared 已存在，跳过运行时安装。来源={Source}。", availability.Source);
                progress?.Report(new CloudflaredInstallProgress(
                    CloudflaredInstallStage.Completed,
                    "cloudflared 已可用",
                    Asset.DownloadSize,
                    Asset.DownloadSize,
                    CanCancel: false));
                return;
            }

            activityLogService.Write(
                LogEntryKind.Info,
                "Cloudflared",
                $"开始下载 cloudflared {Asset.Version}（{Asset.RuntimeIdentifier}）");
            logger.LogInformation(
                "开始安装 cloudflared。版本={Version}，运行时={RuntimeIdentifier}，下载大小={DownloadSize}。",
                Asset.Version,
                Asset.RuntimeIdentifier,
                Asset.DownloadSize);
            var payloadPath = Path.Combine(workspace.CurrentDirectory, Asset.FileName);
            if (!await ValidatePayloadAsync(payloadPath, linkedCancellation.Token))
            {
                TryDeleteFile(payloadPath, "无效的完整下载载荷");
                await DownloadAsync(payloadPath, progress, linkedCancellation.Token);
            }
            else
            {
                logger.LogInformation("复用当前进程内已完整校验的 cloudflared 下载载荷。");
            }

            progress?.Report(new CloudflaredInstallProgress(
                CloudflaredInstallStage.Verifying,
                "正在校验 cloudflared 下载文件…",
                Asset.DownloadSize,
                Asset.DownloadSize));
            if (!await ValidatePayloadAsync(payloadPath, linkedCancellation.Token))
            {
                throw new InvalidDataException("cloudflared 下载文件完整性校验失败");
            }

            await managedInstaller.InstallAsync(payloadPath, progress, linkedCancellation.Token);
            progress?.Report(new CloudflaredInstallProgress(
                CloudflaredInstallStage.Completed,
                "cloudflared 下载并安装完成",
                Asset.DownloadSize,
                Asset.DownloadSize,
                CanCancel: false));
            activityLogService.Write(
                LogEntryKind.Info,
                "Cloudflared",
                $"cloudflared {Asset.Version} 下载并安装完成");
            logger.LogInformation(
                "cloudflared 安装完成。版本={Version}，运行时={RuntimeIdentifier}。",
                Asset.Version,
                Asset.RuntimeIdentifier);
            workspace.CleanupAndRenew("安装成功");
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            if (_shutdown.IsCancellationRequested)
            {
                logger.LogInformation("应用退出，正在终止并清理 cloudflared 下载。");
            }
            else
            {
                logger.LogInformation(
                    "用户取消 cloudflared 下载，当前进程保留下载片段。保留字节={PreservedBytes}。",
                    workspace.GetPreservedBytes(Asset));
                activityLogService.Write(
                    LogEntryKind.Info,
                    "Cloudflared",
                    "已取消 cloudflared 下载；本次运行期间再次下载将尝试续传");
            }

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "cloudflared 下载或安装失败，将清理临时文件。版本={Version}，运行时={RuntimeIdentifier}。",
                Asset.Version,
                Asset.RuntimeIdentifier);
            activityLogService.Write(
                LogEntryKind.Warning,
                "Cloudflared",
                $"cloudflared 下载或安装失败：{exception.Message}");
            workspace.CleanupAndRenew("终态失败");
            throw;
        }
        finally
        {
            Volatile.Write(ref _transferActions, (int)CloudflaredTransferActions.None);
            if (_transferController.IsPaused)
            {
                _transferController.TryResume();
            }

            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        var operationCompleted = _operationGate.Wait(TimeSpan.FromSeconds(5));
        if (operationCompleted)
        {
            try
            {
                workspace.Cleanup("服务释放");
            }
            finally
            {
                _operationGate.Release();
            }
        }

        else
        {
            logger.LogWarning("释放 cloudflared 安装服务时活动操作未在 5 秒内结束，将由进程退出完成资源回收。");
        }

        if (operationCompleted)
        {
            _transferController.Dispose();
            _shutdown.Dispose();
            _operationGate.Dispose();
        }
    }

    private async Task DownloadAsync(
        string payloadPath,
        IProgress<CloudflaredInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloadProgress = new DownloadProgressReporter(this, progress);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await downloader.DownloadAsync(
                    Asset.ToReleaseAssetInfo(),
                    payloadPath,
                    downloadProgress,
                    cancellationToken,
                    _transferController,
                    ReleaseAssetPartialRetentionPolicy.PreserveUntilCallerCleanup);
                Volatile.Write(ref _transferActions, (int)CloudflaredTransferActions.None);
                return;
            }
            catch (ReleaseAssetDownloadInterruptedException exception)
            {
                _transferController.TryPause();
                logger.LogWarning(
                    exception,
                    "cloudflared 自动续传失败，等待用户继续。保留字节={PreservedBytes}，可断点续传={CanResume}。",
                    exception.PreservedBytes,
                    exception.CanResume);
                ReportDownloadProgress(
                    progress,
                    new CloudflaredInstallProgress(
                        CloudflaredInstallStage.Paused,
                        exception.CanResume
                            ? "自动续传 3 次仍失败，已保留下载进度；请点击继续下载"
                            : "自动重试 3 次仍失败；请点击继续下载",
                        exception.PreservedBytes,
                        Asset.DownloadSize,
                        CanCancel: true,
                        CanResume: true));
                await _transferController.WaitWhilePausedAsync(cancellationToken);
            }
        }
    }

    private async Task<bool> ValidatePayloadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Asset.DownloadSize)
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            return hash.Equals(Asset.DownloadSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "校验当前进程 cloudflared 下载载荷时失败。");
            return false;
        }
    }

    private void ReportDownloadProgress(
        IProgress<CloudflaredInstallProgress>? progress,
        CloudflaredInstallProgress value)
    {
        var actions = CloudflaredTransferActions.None;
        if (value.CanPause)
        {
            actions |= CloudflaredTransferActions.Pause;
        }

        if (value.CanResume)
        {
            actions |= CloudflaredTransferActions.Resume;
        }

        Volatile.Write(ref _transferActions, (int)actions);
        progress?.Report(value);
    }

    private void MapAndReportDownloadProgress(
        IProgress<CloudflaredInstallProgress>? progress,
        ReleaseAssetDownloadProgress value)
        => ReportDownloadProgress(progress, MapDownloadProgress(value));

    private static CloudflaredInstallProgress MapDownloadProgress(ReleaseAssetDownloadProgress value)
    {
        var (stage, status, canPause, canResume) = value.State switch
        {
            ReleaseAssetDownloadState.Connecting =>
                (CloudflaredInstallStage.Connecting, "正在连接 Cloudflare 官方 GitHub Release…", true, false),
            ReleaseAssetDownloadState.Paused =>
                (CloudflaredInstallStage.Paused, "下载已暂停，进度已保留", false, true),
            ReleaseAssetDownloadState.Retrying =>
                (CloudflaredInstallStage.Retrying,
                    $"下载中断，正在进行第 {value.RetryAttempt} 次自动续传…",
                    true,
                    false),
            ReleaseAssetDownloadState.Restarting =>
                (CloudflaredInstallStage.Downloading,
                    "服务器未接受断点续传，正在安全地重新下载…",
                    true,
                    false),
            ReleaseAssetDownloadState.Verifying =>
                (CloudflaredInstallStage.Verifying,
                    "正在校验 cloudflared 下载文件…",
                    false,
                    false),
            _ => (CloudflaredInstallStage.Downloading, "正在下载 cloudflared…", true, false)
        };
        return new CloudflaredInstallProgress(
            stage,
            status,
            value.DownloadedBytes,
            value.TotalBytes,
            CanCancel: true,
            CanPause: canPause,
            CanResume: canResume);
    }

    private bool HasTransferAction(CloudflaredTransferActions action)
        => ((CloudflaredTransferActions)Volatile.Read(ref _transferActions)).HasFlag(action);

    private async Task CleanupObsoleteManagedInstallsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.ManagedInstallRoot))
        {
            return;
        }

        CloudflaredFileSystemSafety.EnsureRootIsNotLink(paths.ManagedInstallRoot);
        var bundledAvailable = await locator.ValidateDirectoryAsync(
            paths.BundledDirectory,
            cancellationToken);
        foreach (var versionEntry in Directory.EnumerateFileSystemEntries(paths.ManagedInstallRoot).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionName = Path.GetFileName(versionEntry);
            if (!CloudflaredFileSystemSafety.IsDirectoryWithoutLinks(versionEntry) ||
                !string.Equals(versionName, Asset.Version, StringComparison.Ordinal) ||
                bundledAvailable)
            {
                DeleteManagedEntry(versionEntry, bundledAvailable
                    ? "应用已包含有效 cloudflared，清理重复用户级副本"
                    : "清理旧版本或异常用户级 cloudflared 条目");
                continue;
            }

            await MaintainCurrentVersionDirectoryAsync(versionEntry, cancellationToken);
        }

        locator.Invalidate();
    }

    private async Task MaintainCurrentVersionDirectoryAsync(
        string versionDirectory,
        CancellationToken cancellationToken)
    {
        var finalDirectory = paths.GetManagedInstallDirectory(Asset);
        var backupPrefix = Asset.RuntimeIdentifier + ".backup-";
        var currentValid = await TryValidateManagedDirectoryAsync(finalDirectory, cancellationToken);
        if (!currentValid)
        {
            var backupCandidates = Directory.EnumerateFileSystemEntries(versionDirectory)
                .Where(entry => Path.GetFileName(entry).StartsWith(backupPrefix, StringComparison.Ordinal))
                .ToArray();
            foreach (var backupDirectory in backupCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await TryValidateManagedDirectoryAsync(backupDirectory, cancellationToken))
                {
                    continue;
                }

                if (CloudflaredFileSystemSafety.EntryExists(finalDirectory))
                {
                    CloudflaredFileSystemSafety.DeleteEntrySafely(
                        paths.ManagedInstallRoot,
                        finalDirectory);
                }

                Directory.Move(backupDirectory, finalDirectory);
                locator.Invalidate();
                currentValid = await TryValidateManagedDirectoryAsync(finalDirectory, cancellationToken);
                if (currentValid)
                {
                    logger.LogInformation(
                        "已在启动维护中从事务备份恢复 cloudflared。安装目录={InstallDirectory}，备份目录={BackupDirectory}。",
                        finalDirectory,
                        backupDirectory);
                }
                else
                {
                    logger.LogWarning(
                        "cloudflared 事务备份移回后未通过复核，将清理该损坏安装。目录={InstallDirectory}。",
                        finalDirectory);
                }

                break;
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(versionDirectory).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentValid && PathsEqual(entry, finalDirectory))
            {
                continue;
            }

            DeleteManagedEntry(
                entry,
                PathsEqual(entry, finalDirectory)
                    ? "清理损坏的当前 cloudflared 安装"
                    : "清理其它架构或安装事务遗留目录");
        }

        if (!currentValid && !Directory.EnumerateFileSystemEntries(versionDirectory).Any())
        {
            DeleteManagedEntry(versionDirectory, "清理空的 cloudflared 版本目录");
        }
    }

    private async Task<bool> TryValidateManagedDirectoryAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        if (!CloudflaredFileSystemSafety.IsDirectoryWithoutLinks(directory))
        {
            return false;
        }

        try
        {
            CloudflaredFileSystemSafety.EnsureNoLinksInExistingPath(
                paths.ManagedInstallRoot,
                directory);
            return await locator.ValidateDirectoryAsync(directory, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "验证 cloudflared 用户级安装或事务备份时失败，将视为无效。目录={Directory}。",
                directory);
            return false;
        }
    }

    private void DeleteManagedEntry(string entry, string reason)
    {
        try
        {
            CloudflaredFileSystemSafety.DeleteEntrySafely(paths.ManagedInstallRoot, entry);
            logger.LogInformation("cloudflared 受管条目已清理。原因={CleanupReason}。", reason);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "cloudflared 受管条目清理失败。原因={CleanupReason}。", reason);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void TryDeleteFile(string path, string reason)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("已清理 cloudflared 下载文件。原因={CleanupReason}。", reason);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "清理 cloudflared 下载文件失败。原因={CleanupReason}。", reason);
        }
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("cloudflared 安装服务尚未启动");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [Flags]
    private enum CloudflaredTransferActions
    {
        None = 0,
        Pause = 1,
        Resume = 2
    }

    private sealed class DownloadProgressReporter(
        CloudflaredInstallService owner,
        IProgress<CloudflaredInstallProgress>? progress) : IProgress<ReleaseAssetDownloadProgress>
    {
        public void Report(ReleaseAssetDownloadProgress value)
            => owner.MapAndReportDownloadProgress(progress, value);
    }
}
