using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class BackupWorkflowService(
    IDataBackupService backupService,
    IWebDavSyncService webDavSyncService,
    IBackupSecretStore secretStore,
    ISettingsService settingsService,
    IBackupFilePickerService filePickerService,
    IBackupDialogService dialogService,
    IBackupDataFlushService dataFlushService,
    IActiveBackupTaskService activeTaskService,
    IStorageChangeDialogService storageDialogService,
    IDataRestoreRestartService restoreRestartService,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    TimeProvider timeProvider) : IBackupWorkflowService
{
    public async Task<bool> ExportLocalAsync(CancellationToken cancellationToken = default)
    {
        var password = await EnsureBackupPasswordAsync(cancellationToken);
        if (password is null)
        {
            return false;
        }

        var suggested = $"IGoLibrary-Ex-{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.igobackup";
        var destination = await filePickerService.PickExportPathAsync(suggested, cancellationToken);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        if (!destination.EndsWith(".igobackup", StringComparison.OrdinalIgnoreCase))
        {
            destination += ".igobackup";
        }

        await dataFlushService.FlushAsync(cancellationToken);
        var result = await backupService.ExportAsync(destination, password, cancellationToken);
        await notificationService.ShowSuccessAsync(
            "全部数据已导出",
            $"已生成 {FormatBytes(result.FileSize)} 的加密备份。请妥善保存备份密码。",
            cancellationToken);
        return true;
    }

    public async Task<bool> ImportLocalAsync(CancellationToken cancellationToken = default)
    {
        var source = await filePickerService.PickImportPathAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var password = await dialogService.RequestPasswordAsync(
            "输入备份密码",
            "密码只用于本次解密。恢复成功后，它会保存为当前设备后续导出和同步所用的备份密码。",
            requireConfirmation: false,
            cancellationToken);
        return password is not null && await PrepareConfirmAndRestoreAsync(
            source,
            password,
            BackupRestoreSource.LocalFile,
            remoteMetadata: null,
            remoteEndpointFingerprint: null,
            remoteFileSha256: null,
            cancellationToken);
    }

    public async Task<bool> DownloadAndRestoreAsync(CancellationToken cancellationToken = default)
    {
        var download = await webDavSyncService.DownloadAsync(cancellationToken);
        var password = await dialogService.RequestPasswordAsync(
            "解密 WebDAV 备份",
            "请输入该远端备份创建时使用的备份密码。密码不会写入日志。",
            requireConfirmation: false,
            cancellationToken);
        if (password is null)
        {
            await webDavSyncService.DiscardDownloadAsync(download.LocalFilePath, CancellationToken.None);
            return false;
        }

        return await PrepareConfirmAndRestoreAsync(
            download.LocalFilePath,
            password,
            BackupRestoreSource.WebDav,
            download.Metadata,
            download.EndpointFingerprint,
            download.FileSha256,
            cancellationToken);
    }

    public async Task<bool> UploadAsync(CancellationToken cancellationToken = default)
    {
        if (await EnsureBackupPasswordAsync(cancellationToken) is null)
        {
            return false;
        }

        await dataFlushService.FlushAsync(cancellationToken);
        try
        {
            await webDavSyncService.UploadAsync(allowOverwrite: false, cancellationToken);
        }
        catch (BackupSyncConflictException)
        {
            if (!await dialogService.ConfirmRemoteOverwriteAsync(
                    webDavSyncService.Status.RemoteMetadata,
                    cancellationToken))
            {
                return false;
            }

            await webDavSyncService.UploadAsync(allowOverwrite: true, cancellationToken);
        }

        await ClearPreviousBackupPasswordAfterUploadAsync();

        await notificationService.ShowSuccessAsync(
            "WebDAV 上传完成",
            "远端单一备份文件已更新。",
            cancellationToken);
        return true;
    }

    public async Task<bool> ChangeBackupPasswordAsync(CancellationToken cancellationToken = default)
    {
        var current = await secretStore.LoadBackupPasswordAsync(cancellationToken);
        if (await secretStore.LoadPreviousBackupPasswordAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                "上次备份密码轮换的远端结果尚未确认，请先手动上传一次以完成密码同步");
        }

        var password = await dialogService.RequestPasswordAsync(
            "设置备份密码",
            "备份使用此密码进行 AES-256-GCM 加密。密码无法找回，且更改密码不会自动修改已有本地备份。",
            requireConfirmation: true,
            cancellationToken);
        if (password is null)
        {
            return false;
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        var decision = current is null
            ? BackupPasswordChangeDecision.SaveOnly
            : await dialogService.ConfirmPasswordChangeAsync(
                !string.IsNullOrWhiteSpace(settings.BackupSync.Endpoint),
                cancellationToken);
        if (decision == BackupPasswordChangeDecision.Cancel)
        {
            return false;
        }

        if (decision != BackupPasswordChangeDecision.SaveAndUpload || current is null)
        {
            await secretStore.SaveBackupPasswordAsync(password, cancellationToken);
        }
        else
        {
            await secretStore.SavePreviousBackupPasswordAsync(current, cancellationToken);
            var uploadAttempted = false;
            try
            {
                await secretStore.SaveBackupPasswordAsync(password, cancellationToken);
                await dataFlushService.FlushAsync(cancellationToken);
                uploadAttempted = true;
                await webDavSyncService.UploadAsync(allowOverwrite: true, cancellationToken);
                await ClearPreviousBackupPasswordAfterUploadAsync();
            }
            catch (Exception ex) when (!uploadAttempted)
            {
                try
                {
                    await secretStore.SaveBackupPasswordAsync(current, CancellationToken.None);
                    await secretStore.ClearPreviousBackupPasswordAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "备份密码轮换在上传前失败，且恢复原密码时发生错误；旧密码仍保存在安全恢复槽中",
                        ex,
                        rollbackException);
                }

                throw;
            }
            catch (Exception ex)
            {
                activityLogService.Write(
                    LogEntryKind.Warning,
                    "Backup",
                    "远端备份密码轮换结果无法确认；本机保留新密码及受保护的旧密码，等待下次成功上传确认");
                throw new InvalidOperationException(
                    "远端是否已接收新密码加密的备份无法确认。本机已保留新密码和受保护的旧密码；请勿再次修改密码，并在网络恢复后手动上传一次。",
                    ex);
            }
        }

        activityLogService.Write(LogEntryKind.Success, "Backup", "备份加密密码已更新");
        await notificationService.ShowSuccessAsync(
            "备份密码已更新",
            decision == BackupPasswordChangeDecision.SaveAndUpload
                ? "新密码已保存，并已重新加密上传远端备份。"
                : "以后新建的备份将使用新密码。",
            cancellationToken);
        return true;
    }

    private async Task ClearPreviousBackupPasswordAfterUploadAsync()
    {
        try
        {
            await secretStore.ClearPreviousBackupPasswordAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            activityLogService.Write(
                LogEntryKind.Warning,
                "Backup",
                $"远端备份已更新，但清理旧备份密码恢复副本失败：{ex.Message}");
        }
    }

    private async Task<bool> PrepareConfirmAndRestoreAsync(
        string sourcePath,
        string password,
        BackupRestoreSource source,
        WebDavRemoteMetadata? remoteMetadata,
        string? remoteEndpointFingerprint,
        string? remoteFileSha256,
        CancellationToken cancellationToken)
    {
        PreparedBackup? prepared = null;
        try
        {
            var activeTasks = activeTaskService.GetActiveTaskNames();
            if (activeTasks.Count > 0)
            {
                if (!await storageDialogService.ConfirmStopTasksAsync(activeTasks, cancellationToken))
                {
                    if (source == BackupRestoreSource.WebDav)
                    {
                        await webDavSyncService.DiscardDownloadAsync(sourcePath, CancellationToken.None);
                    }

                    return false;
                }

                await activeTaskService.StopAllAsync(cancellationToken);
            }

            await dataFlushService.FlushAsync(cancellationToken);
            prepared = await backupService.PrepareImportAsync(sourcePath, password, cancellationToken);
            if (!await dialogService.ConfirmRestoreAsync(prepared, cancellationToken))
            {
                await backupService.DiscardPreparedAsync(prepared.PreparationId, CancellationToken.None);
                return false;
            }

            var transactionId = await backupService.StageRestoreAsync(
                new BackupRestoreRequest(
                    prepared.PreparationId,
                    password,
                    source,
                    remoteMetadata?.ETag,
                    remoteMetadata?.LastModified,
                    remoteMetadata?.ContentLength,
                    remoteEndpointFingerprint,
                    remoteFileSha256),
                cancellationToken);
            activityLogService.Write(LogEntryKind.Info, "Backup", "恢复事务已通过校验，正在重启应用并覆盖数据");
            await restoreRestartService.RestartAsync(transactionId, cancellationToken);
            return true;
        }
        catch
        {
            if (prepared is not null)
            {
                await backupService.DiscardPreparedAsync(prepared.PreparationId, CancellationToken.None);
            }
            else if (source == BackupRestoreSource.WebDav)
            {
                await webDavSyncService.DiscardDownloadAsync(sourcePath, CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<string?> EnsureBackupPasswordAsync(CancellationToken cancellationToken)
    {
        var current = await secretStore.LoadBackupPasswordAsync(cancellationToken);
        if (current is not null)
        {
            return current;
        }

        var password = await dialogService.RequestPasswordAsync(
            "首次设置备份密码",
            "所有本地备份与 WebDAV 备份都会使用此密码加密。密码无法找回，请妥善保存。",
            requireConfirmation: true,
            cancellationToken);
        if (password is not null)
        {
            await secretStore.SaveBackupPasswordAsync(password, cancellationToken);
        }

        return password;
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F2} MiB"
            : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
}
