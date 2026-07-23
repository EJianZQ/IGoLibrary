using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed class WebDavSyncService(
    ISettingsService settingsService,
    IDataBackupService dataBackupService,
    IPersistentDataFingerprintProvider fingerprintProvider,
    IBackupSecretStore backupSecretStore,
    IPersistentDataChangeTracker changeTracker,
    WebDavClient webDavClient,
    StorageLocations locations,
    IActivityLogService activityLogService,
    ILogger<WebDavSyncService> logger,
    ILogger<WebDavSyncStateStore> stateLogger,
    TimeProvider timeProvider) : IWebDavSyncService
{
    private readonly BackupWorkspaceManager _workspaceManager = new(locations);
    private readonly WebDavSyncStateStore _stateStore = new(locations, stateLogger);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private BackupSyncRuntimeStatus _status = BackupSyncRuntimeStatus.Idle;

    public BackupSyncRuntimeStatus Status => changeTracker.IsAutomaticUploadPaused
        ? _status with
        {
            HasConflict = true,
            Message = changeTracker.AutomaticUploadPauseReason ?? "自动上传已暂停，需要手动确认"
        }
        : _status;

    public event EventHandler<BackupSyncRuntimeStatus>? StatusChanged;

    public async Task ReconcileLocalStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = BackupSyncSettings.Normalize(
                (await settingsService.LoadAsync(cancellationToken)).BackupSync);
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                return;
            }

            var context = await CreateContextAsync(requireBackupPassword: false, cancellationToken);
            var state = await _stateStore.LoadAsync(cancellationToken);
            if (state is not null &&
                string.Equals(state.EndpointFingerprint, context.EndpointFingerprint, StringComparison.Ordinal))
            {
                RestorePersistedStatus(state);
            }

            if (!settings.AutoUploadEnabled)
            {
                return;
            }

            var reconciliationVersion = changeTracker.Version;
            var localFingerprint = await fingerprintProvider.ComputeAsync(cancellationToken);
            if (state is not null &&
                string.Equals(state.EndpointFingerprint, context.EndpointFingerprint, StringComparison.Ordinal) &&
                string.Equals(state.LocalSemanticFingerprint, localFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                changeTracker.MarkSynchronized(reconciliationVersion);
                return;
            }

            if (!changeTracker.IsDirty)
            {
                changeTracker.MarkChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "协调本地语义指纹失败；已保留现有未同步状态。");
        }
    }

    private void RestorePersistedStatus(WebDavSyncState state)
    {
        if (state.LastSuccessfulSync is null)
        {
            return;
        }

        SetStatus(new BackupSyncRuntimeStatus(
            false,
            false,
            "已加载上次 WebDAV 同步状态",
            state.LastSuccessfulSync,
            new WebDavRemoteMetadata(
                true,
                state.ContentLength,
                state.ETag,
                state.LastModified)));
    }

    public async Task RecordRestoredBaselineAsync(
        string semanticFingerprint,
        WebDavRemoteMetadata metadata,
        string expectedEndpointFingerprint,
        string remoteFileSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEndpointFingerprint);
        if (remoteFileSha256.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new ArgumentException("远端备份哈希格式无效", nameof(remoteFileSha256));
        }
        var context = await CreateContextAsync(requireBackupPassword: false, cancellationToken);
        if (!string.Equals(
                context.EndpointFingerprint,
                expectedEndpointFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("恢复后的 WebDAV 端点与本次下载端点不同，不能自动建立同步基线");
        }
        var restoredBaselineVersion = changeTracker.Version;
        var now = timeProvider.GetUtcNow();
        await _stateStore.SaveAsync(
            new WebDavSyncState(
                context.EndpointFingerprint,
                metadata.ETag,
                metadata.LastModified,
                metadata.ContentLength,
                remoteFileSha256,
                semanticFingerprint,
                now),
            cancellationToken);
        changeTracker.MarkSynchronized(restoredBaselineVersion);
        SetStatus(new BackupSyncRuntimeStatus(
            false,
            false,
            "已根据下载恢复建立 WebDAV 同步基线",
            now,
            metadata));
    }

    public async Task<WebDavRemoteMetadata> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N");
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("WebDAV 连接测试已开始。操作 ID={OperationId}。", operationId);
            activityLogService.Write(LogEntryKind.Info, "Backup", "正在测试 WebDAV 连接与读写权限");
            SetStatus(_status with { IsBusy = true, Message = "正在测试 WebDAV 连接" });
            var context = await CreateContextAsync(requireBackupPassword: false, cancellationToken);
            using var client = webDavClient.CreateHttpClient(
                context.Username,
                context.Password,
                context.TlsVerifyMode);
            await webDavClient.EnsureCollectionsAsync(
                client,
                context.EndpointUri,
                context.RemotePath,
                cancellationToken);
            await webDavClient.ProbeWriteAsync(client, context.FileUri, cancellationToken);
            var metadata = await webDavClient.GetMetadataAsync(client, context.FileUri, cancellationToken);
            SetStatus(new BackupSyncRuntimeStatus(
                false,
                false,
                "WebDAV 连接及读写权限正常",
                _status.LastSuccessfulSync,
                metadata));
            activityLogService.Write(LogEntryKind.Success, "Backup", "WebDAV 连接及读写权限测试成功");
            logger.LogInformation("WebDAV 连接测试已完成。操作 ID={OperationId}。", operationId);
            return metadata;
        }
        catch (Exception ex)
        {
            SetStatus(_status with { IsBusy = false, Message = $"WebDAV 连接测试失败：{ex.Message}" });
            logger.LogError(ex, "WebDAV 连接测试失败。操作 ID={OperationId}。", operationId);
            activityLogService.Write(LogEntryKind.Error, "Backup", $"WebDAV 连接测试失败：{ex.Message}");
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<WebDavUploadResult> UploadAsync(
        bool allowOverwrite,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N");
        await _operationGate.WaitAsync(cancellationToken);
        string? workspace = null;
        try
        {
            SetStatus(_status with { IsBusy = true, Message = "正在上传全部应用数据" });
            activityLogService.Write(LogEntryKind.Info, "Backup", "正在将全部应用数据上传到 WebDAV");
            var context = await CreateContextAsync(requireBackupPassword: true, cancellationToken);
            using var client = webDavClient.CreateHttpClient(
                context.Username,
                context.Password,
                context.TlsVerifyMode);
            await webDavClient.EnsureCollectionsAsync(
                client,
                context.EndpointUri,
                context.RemotePath,
                cancellationToken);
            var before = await webDavClient.GetMetadataAsync(client, context.FileUri, cancellationToken);
            SetStatus(_status with { RemoteMetadata = before });
            var state = await _stateStore.LoadAsync(cancellationToken);
            await EnsureUploadAllowedAsync(
                client,
                context,
                before,
                state,
                allowOverwrite,
                cancellationToken);

            workspace = _workspaceManager.Create("webdav", operationId);
            var localFile = Path.Combine(workspace, "upload.igobackup");
            var trackedVersion = changeTracker.Version;
            var export = await dataBackupService.ExportAsync(
                localFile,
                context.BackupPassword!,
                cancellationToken);
            await webDavClient.PutFileAsync(
                client,
                context.FileUri,
                localFile,
                before,
                cancellationToken);
            var after = await webDavClient.GetMetadataAsync(client, context.FileUri, cancellationToken);
            var fileHash = await WebDavClient.HashFileAsync(localFile, cancellationToken);
            var now = timeProvider.GetUtcNow();
            await _stateStore.SaveAsync(
                new WebDavSyncState(
                    context.EndpointFingerprint,
                    after.ETag,
                    after.LastModified,
                    after.ContentLength,
                    fileHash,
                    export.Manifest.SemanticFingerprint,
                    now),
                cancellationToken);
            changeTracker.MarkSynchronized(trackedVersion);
            try
            {
                await backupSecretStore.ClearPreviousBackupPasswordAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WebDAV 上传成功，但无法删除先前的备份密码恢复副本。");
            }

            var result = new WebDavUploadResult(after, export.Manifest, operationId);
            SetStatus(new BackupSyncRuntimeStatus(
                false,
                false,
                "WebDAV 上传成功",
                now,
                after));
            activityLogService.Write(LogEntryKind.Success, "Backup", "全部应用数据已上传到 WebDAV");
            logger.LogInformation(
                "WebDAV 上传已完成。操作 ID={OperationId}，端点={Endpoint}，字节数={Bytes}，ETag={ETag}。",
                operationId,
                context.SafeEndpoint,
                after.ContentLength,
                after.ETag);
            return result;
        }
        catch (BackupSyncConflictException ex)
        {
            SetStatus(_status with
            {
                IsBusy = false,
                HasConflict = true,
                Message = ex.Message
            });
            activityLogService.Write(LogEntryKind.Warning, "Backup", ex.Message);
            logger.LogWarning(ex, "WebDAV 上传发生冲突。操作 ID={OperationId}。", operationId);
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(_status with { IsBusy = false, Message = $"WebDAV 上传失败：{ex.Message}" });
            logger.LogError(ex, "WebDAV 上传失败。操作 ID={OperationId}。", operationId);
            activityLogService.Write(LogEntryKind.Error, "Backup", $"WebDAV 上传失败：{ex.Message}");
            throw;
        }
        finally
        {
            TryDeleteWorkspace(workspace);
            _operationGate.Release();
        }
    }

    public async Task<WebDavDownloadResult> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N");
        await _operationGate.WaitAsync(cancellationToken);
        string? workspace = null;
        try
        {
            SetStatus(_status with { IsBusy = true, Message = "正在下载 WebDAV 备份" });
            activityLogService.Write(LogEntryKind.Info, "Backup", "正在下载 WebDAV 远端备份");
            var context = await CreateContextAsync(requireBackupPassword: false, cancellationToken);
            using var client = webDavClient.CreateHttpClient(
                context.Username,
                context.Password,
                context.TlsVerifyMode);
            var metadata = await webDavClient.GetMetadataAsync(client, context.FileUri, cancellationToken);
            if (!metadata.Exists)
            {
                throw new FileNotFoundException("WebDAV 远端备份不存在");
            }

            if (metadata.ContentLength > EncryptedBackupArchiveCodec.MaximumArchiveSize)
            {
                throw new InvalidDataException("WebDAV 远端备份超过 2 GiB 限制");
            }

            workspace = _workspaceManager.Create("webdav", operationId);
            var localFile = Path.Combine(workspace, "download.igobackup");
            await webDavClient.DownloadFileAsync(client, context.FileUri, localFile, cancellationToken);
            var fileSha256 = await WebDavClient.HashFileAsync(localFile, cancellationToken);
            workspace = null;
            SetStatus(_status with
            {
                IsBusy = false,
                Message = "WebDAV 备份已下载，等待对比确认",
                RemoteMetadata = metadata
            });
            logger.LogInformation(
                "WebDAV 下载已完成。操作 ID={OperationId}，端点={Endpoint}，字节数={Bytes}，ETag={ETag}。",
                operationId,
                context.SafeEndpoint,
                new FileInfo(localFile).Length,
                metadata.ETag);
            activityLogService.Write(LogEntryKind.Success, "Backup", "WebDAV 远端备份下载完成，等待对比确认");
            return new WebDavDownloadResult(
                localFile,
                metadata,
                context.EndpointFingerprint,
                fileSha256,
                operationId);
        }
        catch (Exception ex)
        {
            SetStatus(_status with { IsBusy = false, Message = $"WebDAV 下载失败：{ex.Message}" });
            logger.LogError(ex, "WebDAV 下载失败。操作 ID={OperationId}。", operationId);
            activityLogService.Write(LogEntryKind.Error, "Backup", $"WebDAV 下载失败：{ex.Message}");
            TryDeleteWorkspace(workspace);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task DiscardDownloadAsync(
        string localFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(localFilePath);
        var workspace = Directory.GetParent(fullPath)?.FullName;
        var category = workspace is null ? null : Directory.GetParent(workspace)?.FullName;
        var expectedCategory = Path.Combine(_workspaceManager.Root, "webdav");
        if (workspace is null || category is null ||
            !string.Equals(
                Path.TrimEndingDirectorySeparator(category),
                Path.TrimEndingDirectorySeparator(expectedCategory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(workspace), "N", out _) ||
            !string.Equals(Path.GetFileName(fullPath), "download.igobackup", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("下载工作区路径无效");
        }

        TryDeleteWorkspace(workspace);
        return Task.CompletedTask;
    }

    private async Task<WebDavContext> CreateContextAsync(
        bool requireBackupPassword,
        CancellationToken cancellationToken)
    {
        var settings = BackupSyncSettings.Normalize((await settingsService.LoadAsync(cancellationToken)).BackupSync);
        if (!BackupSyncSettings.TryValidateEndpoint(
                settings.Endpoint,
                settings.AllowInsecureHttp,
                out var endpointUri,
                out var endpointError))
        {
            throw new InvalidOperationException(endpointError);
        }

        if (!BackupSyncSettings.TryValidateRemoteDirectory(
                settings.RemoteDirectory,
                out var remoteDirectory,
                out var directoryError))
        {
            throw new InvalidOperationException(directoryError);
        }

        var remotePath = BackupSyncSettings.BuildRemotePath(remoteDirectory);

        var webDavPassword = await backupSecretStore.LoadWebDavPasswordAsync(cancellationToken);
        if (string.IsNullOrEmpty(settings.Username) != string.IsNullOrEmpty(webDavPassword))
        {
            throw new InvalidOperationException("WebDAV 用户名和密码必须同时填写，或同时留空使用匿名访问");
        }

        string? backupPassword = null;
        if (requireBackupPassword)
        {
            backupPassword = await backupSecretStore.LoadBackupPasswordAsync(cancellationToken)
                             ?? throw new InvalidOperationException("尚未设置备份加密密码");
            BackupPasswordRules.Validate(backupPassword);
        }

        var fileUri = WebDavClient.BuildFileUri(endpointUri!, remotePath);
        return new WebDavContext(
            endpointUri!,
            fileUri,
            remotePath,
            settings.Username,
            webDavPassword,
            backupPassword,
            settings.TlsVerifyMode,
            WebDavSyncStateStore.GetEndpointFingerprint(endpointUri!, remotePath, settings.Username),
            $"{endpointUri!.Scheme}://{endpointUri.Host}:{endpointUri.Port}/{HashRemotePath(remotePath)}");
    }

    private async Task EnsureUploadAllowedAsync(
        HttpClient client,
        WebDavContext context,
        WebDavRemoteMetadata remote,
        WebDavSyncState? state,
        bool allowOverwrite,
        CancellationToken cancellationToken)
    {
        if (!remote.Exists || allowOverwrite)
        {
            return;
        }

        if (state is null ||
            !string.Equals(state.EndpointFingerprint, context.EndpointFingerprint, StringComparison.Ordinal))
        {
            throw new BackupSyncConflictException("远端备份已存在或已被其他设备更新，自动上传已暂停，请先下载对比或手动确认覆盖");
        }

        var comparison = CompareRemoteBaseline(remote, state);
        if (comparison == WebDavBaselineComparison.Mismatch)
        {
            throw new BackupSyncConflictException("远端备份已被其他设备更新，自动上传已暂停，请先下载对比或手动确认覆盖");
        }

        if (comparison == WebDavBaselineComparison.StrongMatch)
        {
            return;
        }

        if (string.IsNullOrEmpty(state.RemoteFileSha256) ||
            !string.Equals(
                await webDavClient.HashRemoteFileAsync(client, context.FileUri, cancellationToken),
                state.RemoteFileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupSyncConflictException("远端内容哈希与同步基线不一致，自动上传已暂停");
        }

        if (!WebDavClient.TryGetStrongETag(remote.ETag, out _))
        {
            throw new BackupSyncConflictException(
                "WebDAV 服务未提供强 ETag，虽然远端内容仍与基线一致，但无法安全防止上传竞态；请手动确认覆盖");
        }
    }

    internal static WebDavBaselineComparison CompareRemoteBaseline(
        WebDavRemoteMetadata remote,
        WebDavSyncState state)
    {
        if (remote.ContentLength.HasValue &&
            state.ContentLength.HasValue &&
            remote.ContentLength != state.ContentLength)
        {
            return WebDavBaselineComparison.Mismatch;
        }

        if (WebDavClient.TryGetStrongETag(remote.ETag, out var remoteETag) &&
            WebDavClient.TryGetStrongETag(state.ETag, out var stateETag))
        {
            return string.Equals(remoteETag, stateETag, StringComparison.Ordinal)
                ? WebDavBaselineComparison.StrongMatch
                : WebDavBaselineComparison.Mismatch;
        }

        return WebDavBaselineComparison.RequiresContentHash;
    }

    private void SetStatus(BackupSyncRuntimeStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void TryDeleteWorkspace(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        try
        {
            _workspaceManager.Delete(workspace);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理 WebDAV 工作区失败。");
        }
    }

    private static string HashRemotePath(string path)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..12];

    private sealed record WebDavContext(
        Uri EndpointUri,
        Uri FileUri,
        string RemotePath,
        string Username,
        string? Password,
        string? BackupPassword,
        WebDavTlsVerifyMode TlsVerifyMode,
        string EndpointFingerprint,
        string SafeEndpoint);
}

internal enum WebDavBaselineComparison
{
    StrongMatch,
    RequiresContentHash,
    Mismatch
}
