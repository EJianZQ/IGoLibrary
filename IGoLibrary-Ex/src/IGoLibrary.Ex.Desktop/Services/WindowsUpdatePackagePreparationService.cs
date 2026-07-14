using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WindowsUpdatePackagePreparationService(
    IReleaseAssetDownloader downloader,
    WindowsUpdateWorkspaceManager workspaceManager,
    ILogger<WindowsUpdatePackagePreparationService> logger)
{
    private const long DiskSpaceMargin = 256L * 1024 * 1024;

    public string ValidateInstallationDirectory(string currentVersion)
    {
        var installationDirectory = UpdatePathSafety.EnsureNotFileSystemRoot(
            AppContext.BaseDirectory,
            allowExistingRoot: true);
        UpdatePathSafety.RejectReparsePoint(installationDirectory);

        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定当前主程序路径");
        if (!string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(executablePath)),
                installationDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFileName(executablePath),
                UpdateProtocol.EntryExecutableName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前程序不是受支持的 Windows 绿色版发布包");
        }

        var manifestPath = Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName);
        UpdatePackageValidator.LoadAndValidateManifest(manifestPath, currentVersion);
        UpdatePackageValidator.ValidatePortableMarker(installationDirectory);
        var updaterPath = Path.Combine(installationDirectory, UpdateProtocol.UpdaterExecutableName);
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException(
                "当前绿色版缺少独立更新组件，请先手动安装带自动更新功能的版本",
                updaterPath);
        }

        return installationDirectory;
    }

    public async Task<PreparedWindowsUpdatePackage> PrepareAsync(
        WindowsUpdateWorkspace workspace,
        string installationDirectory,
        ReleaseAssetInfo asset,
        string currentVersion,
        string targetVersion,
        ReleaseAssetDownloadPauseController transferController,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        if (workspace.IsVerifiedCache)
        {
            WindowsUpdateProgressReporter.Report(
                progress,
                WindowsUpdateStage.Verifying,
                "已找到校验通过的更新缓存，正在复核…",
                actions: WindowsUpdateAvailableActions.Cancel);
            logger.LogInformation(
                "正在复用已验签更新缓存。事务={TransactionId}，目标版本={TargetVersion}。",
                workspace.TransactionId,
                targetVersion);
        }
        else
        {
            EnsureAvailableSpace(
                workspaceManager.UpdatesRoot,
                asset.Size + DiskSpaceMargin,
                "更新下载目录");
            logger.LogInformation(
                "开始下载更新包。事务={TransactionId}，目标版本={TargetVersion}，资源={AssetName}，保存路径={ArchivePath}。",
                workspace.TransactionId,
                targetVersion,
                asset.Name,
                workspace.ArchivePath);
            await DownloadWithManualRecoveryAsync(
                workspace,
                asset,
                transferController,
                progress,
                cancellationToken);
            logger.LogInformation(
                "更新包下载完成。事务={TransactionId}，目标版本={TargetVersion}，实际大小={DownloadedSize}字节。",
                workspace.TransactionId,
                targetVersion,
                new FileInfo(workspace.ArchivePath).Length);

            WindowsUpdateProgressReporter.Report(
                progress,
                WindowsUpdateStage.Verifying,
                "下载完成，正在校验更新包…",
                actions: WindowsUpdateAvailableActions.Cancel);
            await WindowsUpdateWorkspaceManager.VerifyArchiveDigestAsync(
                workspace.ArchivePath,
                asset,
                cancellationToken);
            logger.LogInformation(
                "更新包 SHA-256 校验通过。事务={TransactionId}，摘要={PackageDigest}。",
                workspace.TransactionId,
                asset.Digest);

            var extractProgress = new Progress<(long Completed, long Total)>(value =>
                WindowsUpdateProgressReporter.Report(
                    progress,
                    WindowsUpdateStage.Extracting,
                    "正在安全解压并验证程序文件…",
                    value.Completed,
                    value.Total,
                    actions: WindowsUpdateAvailableActions.Cancel));
            var manifest = await UpdatePackageValidator.ExtractAndValidateAsync(
                workspace.ArchivePath,
                workspace.StagingDirectory,
                targetVersion,
                extractProgress,
                cancellationToken);
            logger.LogInformation(
                "更新包解压及清单校验通过。事务={TransactionId}，版本={ManifestVersion}，文件数={ManifestFileCount}，暂存目录={StagingDirectory}。",
                workspace.TransactionId,
                manifest.Version,
                manifest.Files.Count,
                workspace.StagingDirectory);

            workspaceManager.WriteVerifiedMarker(workspace, targetVersion, asset);
            logger.LogInformation(
                "已记录完整验签更新缓存。事务={TransactionId}，目标版本={TargetVersion}。",
                workspace.TransactionId,
                targetVersion);
        }

        WindowsUpdateProgressReporter.Report(
            progress,
            WindowsUpdateStage.Verifying,
            "正在验证安装空间和当前程序完整性…",
            actions: WindowsUpdateAvailableActions.Cancel);
        await ValidateSpaceAndCurrentPackageAsync(
            installationDirectory,
            workspace.StagingDirectory,
            currentVersion,
            workspaceManager.UpdatesRoot,
            asset.Size,
            cancellationToken);
        logger.LogInformation(
            "当前安装包完整性和磁盘空间检查通过。事务={TransactionId}，目标版本={TargetVersion}。",
            workspace.TransactionId,
            targetVersion);

        return new PreparedWindowsUpdatePackage(
            workspace,
            installationDirectory,
            asset,
            currentVersion,
            targetVersion);
    }

    private async Task DownloadWithManualRecoveryAsync(
        WindowsUpdateWorkspace workspace,
        ReleaseAssetInfo asset,
        ReleaseAssetDownloadPauseController transferController,
        IProgress<WindowsUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var downloadProgress = new Progress<ReleaseAssetDownloadProgress>(value =>
                    ReportDownloadProgress(progress, value));
                await downloader.DownloadAsync(
                    asset,
                    workspace.ArchivePath,
                    downloadProgress,
                    cancellationToken,
                    transferController);
                return;
            }
            catch (ReleaseAssetDownloadInterruptedException exception)
            {
                transferController.TryPause();
                logger.LogWarning(
                    exception,
                    "更新包自动续传失败，等待用户继续。事务={TransactionId}，保留字节={PreservedBytes}，可断点续传={CanResume}。",
                    workspace.TransactionId,
                    exception.PreservedBytes,
                    exception.CanResume);
                WindowsUpdateProgressReporter.Report(
                    progress,
                    WindowsUpdateStage.Downloading,
                    exception.CanResume
                        ? "自动续传 3 次仍失败，已保留下载进度；请点击继续下载"
                        : "自动重试 3 次仍失败；请点击继续下载",
                    exception.PreservedBytes,
                    asset.Size,
                    WindowsUpdateTransferState.AwaitingManualResume,
                    WindowsUpdateAvailableActions.Resume | WindowsUpdateAvailableActions.Cancel);
                await transferController.WaitWhilePausedAsync(cancellationToken);
                logger.LogInformation(
                    "用户请求继续更新包下载。事务={TransactionId}，续传偏移={ResumeOffset}。",
                    workspace.TransactionId,
                    exception.PreservedBytes);
            }
        }
    }

    private static void ReportDownloadProgress(
        IProgress<WindowsUpdateProgress> progress,
        ReleaseAssetDownloadProgress value)
    {
        var (status, transferState, actions, stage) = value.State switch
        {
            ReleaseAssetDownloadState.Connecting =>
                ("正在连接 GitHub 下载新版本…",
                    WindowsUpdateTransferState.Connecting,
                    WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Downloading),
            ReleaseAssetDownloadState.Downloading =>
                ("正在从 GitHub 下载新版本…",
                    WindowsUpdateTransferState.Downloading,
                    WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Downloading),
            ReleaseAssetDownloadState.Paused =>
                ("下载已暂停，进度已保留",
                    WindowsUpdateTransferState.Paused,
                    WindowsUpdateAvailableActions.Resume | WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Downloading),
            ReleaseAssetDownloadState.Retrying =>
                ($"下载中断，{Math.Max(0, value.RetryDelay?.TotalSeconds ?? 0):F0} 秒后自动续传（{value.RetryAttempt}/3）…",
                    WindowsUpdateTransferState.Retrying,
                    WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Downloading),
            ReleaseAssetDownloadState.Restarting =>
                ("服务器未接受断点续传，正在从零重新下载…",
                    WindowsUpdateTransferState.Downloading,
                    WindowsUpdateAvailableActions.Pause | WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Downloading),
            _ =>
                ("下载完成，正在校验更新包…",
                    WindowsUpdateTransferState.Verifying,
                    WindowsUpdateAvailableActions.Cancel,
                    WindowsUpdateStage.Verifying)
        };
        WindowsUpdateProgressReporter.Report(
            progress,
            stage,
            status,
            value.DownloadedBytes,
            value.TotalBytes,
            transferState,
            actions);
    }

    private static async Task ValidateSpaceAndCurrentPackageAsync(
        string installationDirectory,
        string stagingDirectory,
        string currentVersion,
        string updatesRoot,
        long archiveBytes,
        CancellationToken cancellationToken)
    {
        var currentManifest = UpdatePackageValidator.LoadAndValidateManifest(
            Path.Combine(installationDirectory, UpdateProtocol.ManifestFileName),
            currentVersion);
        await UpdatePackageValidator.ValidateDirectoryAsync(
            installationDirectory,
            currentManifest,
            allowAdditionalFiles: true,
            cancellationToken);

        var owned = currentManifest.Files
            .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        owned.Add(UpdateProtocol.ManifestFileName);
        long unknownBytes = 0;
        foreach (var path in EnumerateFiles(installationDirectory))
        {
            var relative = UpdatePathSafety.NormalizeRelativePath(
                Path.GetRelativePath(installationDirectory, path));
            if (!owned.Contains(relative))
            {
                checked
                {
                    unknownBytes += new FileInfo(path).Length;
                }
            }
        }

        long stagingBytes = 0;
        foreach (var path in EnumerateFiles(stagingDirectory))
        {
            checked
            {
                stagingBytes += new FileInfo(path).Length;
            }
        }

        EnsureAvailableSpace(
            installationDirectory,
            checked(stagingBytes + unknownBytes + archiveBytes + DiskSpaceMargin),
            "程序所在磁盘");
        EnsureAvailableSpace(updatesRoot, DiskSpaceMargin, "更新暂存磁盘");
    }

    private static IEnumerable<string> EnumerateFiles(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"目录包含不受支持的链接或联接：{entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void EnsureAvailableSpace(string path, long requiredBytes, string label)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))
                   ?? throw new InvalidOperationException($"无法确定{label}卷");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            var missing = Math.Max(
                1,
                (long)Math.Ceiling(
                    (requiredBytes - drive.AvailableFreeSpace) / 1024d / 1024d));
            throw new IOException($"{label}空间不足，至少还需要 {missing} MiB");
        }
    }
}

internal sealed record PreparedWindowsUpdatePackage(
    WindowsUpdateWorkspace Workspace,
    string InstallationDirectory,
    ReleaseAssetInfo Asset,
    string CurrentVersion,
    string TargetVersion);
