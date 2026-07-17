using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredManagedInstaller
{
    Task InstallAsync(
        string payloadPath,
        IProgress<CloudflaredInstallProgress>? progress,
        CancellationToken cancellationToken);
}

internal sealed class CloudflaredManagedInstaller : ICloudflaredManagedInstaller
{
    private readonly CloudflaredAssetCatalog catalog;
    private readonly ICloudflaredPathProvider paths;
    private readonly ICloudflaredToolLocator locator;
    private readonly ICloudflaredExtractor extractor;
    private readonly ILogger<CloudflaredManagedInstaller> logger;
    private readonly Action<string, string> deleteEntry;

    public CloudflaredManagedInstaller(
        CloudflaredAssetCatalog catalog,
        ICloudflaredPathProvider paths,
        ICloudflaredToolLocator locator,
        ICloudflaredExtractor extractor,
        ILogger<CloudflaredManagedInstaller> logger)
        : this(
            catalog,
            paths,
            locator,
            extractor,
            logger,
            CloudflaredFileSystemSafety.DeleteEntrySafely)
    {
    }

    internal CloudflaredManagedInstaller(
        CloudflaredAssetCatalog catalog,
        ICloudflaredPathProvider paths,
        ICloudflaredToolLocator locator,
        ICloudflaredExtractor extractor,
        ILogger<CloudflaredManagedInstaller> logger,
        Action<string, string> deleteEntry)
    {
        this.catalog = catalog;
        this.paths = paths;
        this.locator = locator;
        this.extractor = extractor;
        this.logger = logger;
        this.deleteEntry = deleteEntry;
    }

    public async Task InstallAsync(
        string payloadPath,
        IProgress<CloudflaredInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var asset = catalog.Current;
        var finalDirectory = paths.GetManagedInstallDirectory(asset);
        var parentDirectory = Path.GetDirectoryName(finalDirectory)
                              ?? throw new InvalidOperationException("无法确定 cloudflared 安装父目录");
        CloudflaredFileSystemSafety.EnsureRootIsNotLink(paths.ManagedInstallRoot);
        Directory.CreateDirectory(paths.ManagedInstallRoot);
        CloudflaredFileSystemSafety.EnsureParentPathIsSafe(paths.ManagedInstallRoot, finalDirectory);
        Directory.CreateDirectory(parentDirectory);
        CloudflaredFileSystemSafety.EnsureParentPathIsSafe(paths.ManagedInstallRoot, finalDirectory);
        if (CloudflaredFileSystemSafety.EntryExists(finalDirectory) &&
            !CloudflaredFileSystemSafety.IsDirectoryWithoutLinks(finalDirectory))
        {
            CloudflaredFileSystemSafety.DeleteEntrySafely(paths.ManagedInstallRoot, finalDirectory);
            logger.LogWarning(
                "已清理占用 cloudflared 当前安装路径的非目录或链接条目。路径={InstallDirectory}。",
                finalDirectory);
        }

        var stagingDirectory = finalDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        var backupDirectory = finalDirectory + ".backup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDirectory);
        var promoted = false;
        var backedUp = false;
        var preserveBackupForRecovery = false;
        try
        {
            progress?.Report(new CloudflaredInstallProgress(
                asset.ArchiveType == "tgz"
                    ? CloudflaredInstallStage.Extracting
                    : CloudflaredInstallStage.Verifying,
                asset.ArchiveType == "tgz"
                    ? "正在安全解压 cloudflared…"
                    : "正在准备 cloudflared 可执行文件…",
                asset.DownloadSize,
                asset.DownloadSize));
            await extractor.PrepareExecutableAsync(
                asset,
                payloadPath,
                Path.Combine(stagingDirectory, asset.ExecutableName),
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(stagingDirectory, "LICENSE.txt"),
                catalog.LicenseBytes,
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(stagingDirectory, "THIRD-PARTY-NOTICES.txt"),
                catalog.NoticesBytes,
                cancellationToken);
            locator.Invalidate();
            if (!await locator.ValidateDirectoryAsync(stagingDirectory, cancellationToken))
            {
                throw new InvalidDataException("准备后的 cloudflared 文件未通过完整性校验");
            }

            logger.LogInformation(
                "cloudflared staging 已通过完整性校验。目录={StagingDirectory}。",
                stagingDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CloudflaredInstallProgress(
                CloudflaredInstallStage.Installing,
                "正在安装 cloudflared…",
                asset.DownloadSize,
                asset.DownloadSize,
                CanCancel: false));
            if (Directory.Exists(finalDirectory))
            {
                Directory.Move(finalDirectory, backupDirectory);
                backedUp = true;
                logger.LogInformation("已为 cloudflared 现有安装创建事务备份。目录={BackupDirectory}。", backupDirectory);
            }

            Directory.Move(stagingDirectory, finalDirectory);
            promoted = true;
            logger.LogInformation("cloudflared staging 已原子提交。目录={InstallDirectory}。", finalDirectory);
            locator.Invalidate();
            if (!await locator.ValidateDirectoryAsync(finalDirectory, CancellationToken.None))
            {
                throw new InvalidDataException("安装后的 cloudflared 文件未通过完整性校验");
            }

            logger.LogInformation("cloudflared 原子安装提交后复核成功。目录={InstallDirectory}。", finalDirectory);
            if (backedUp)
            {
                DeleteManagedDirectory(backupDirectory, "安装提交后的旧版本备份");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "cloudflared 原子安装失败，正在回滚。目录={InstallDirectory}。", finalDirectory);
            try
            {
                if (promoted && CloudflaredFileSystemSafety.EntryExists(finalDirectory))
                {
                    DeleteManagedEntryStrict(finalDirectory);
                }

                if (backedUp && CloudflaredFileSystemSafety.EntryExists(backupDirectory))
                {
                    if (CloudflaredFileSystemSafety.EntryExists(finalDirectory))
                    {
                        DeleteManagedEntryStrict(finalDirectory);
                    }

                    Directory.Move(backupDirectory, finalDirectory);
                    backedUp = false;
                    logger.LogInformation("cloudflared 原安装已从事务备份恢复。目录={InstallDirectory}。", finalDirectory);
                }
            }
            catch (Exception rollbackException)
            {
                preserveBackupForRecovery = backedUp;
                logger.LogError(
                    rollbackException,
                    "cloudflared 安装回滚失败；可恢复备份将保留。安装目录={InstallDirectory}，备份目录={BackupDirectory}。",
                    finalDirectory,
                    backupDirectory);
                locator.Invalidate();
                throw new AggregateException(
                    "cloudflared 安装失败，且旧安装未能自动恢复",
                    exception,
                    rollbackException);
            }

            locator.Invalidate();
            throw;
        }
        finally
        {
            DeleteManagedDirectory(stagingDirectory, "安装 staging");

            if (preserveBackupForRecovery)
            {
                logger.LogWarning(
                    "cloudflared 回滚备份已保留，将在下次启动尝试恢复。目录={BackupDirectory}。",
                    backupDirectory);
            }
            else
            {
                DeleteManagedDirectory(backupDirectory, "安装 backup");
            }
        }
    }

    private void DeleteManagedDirectory(string directory, string reason)
    {
        try
        {
            deleteEntry(paths.ManagedInstallRoot, directory);
            logger.LogInformation("cloudflared 安装事务目录已清理。原因={CleanupReason}。", reason);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "cloudflared 安装事务目录清理失败，将在下次启动重试。原因={CleanupReason}。", reason);
        }
    }

    private void DeleteManagedEntryStrict(string path)
    {
        deleteEntry(paths.ManagedInstallRoot, path);
    }
}
