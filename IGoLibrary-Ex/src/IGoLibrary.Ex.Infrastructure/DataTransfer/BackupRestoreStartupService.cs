using System.Security.Cryptography;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

public sealed class BackupRestoreStartupService(
    SqliteConnectionFactory connectionFactory,
    StorageLocations locations,
    ICredentialStore credentialStore,
    IBackupSecretStore backupSecretStore,
    IWebDavSyncService webDavSyncService,
    IPersistentDataChangeTracker changeTracker,
    IAppVersionProvider appVersionProvider,
    IActivityLogService activityLogService,
    ILogger<BackupRestoreStartupService> logger,
    TimeProvider timeProvider) : IBackupRestoreStartupService
{
    private const string ResultFileName = "last-restore-result.json";
    private readonly BackupWorkspaceManager _workspaceManager = new(locations);
    private readonly EncryptedBackupArchiveCodec _codec = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BackupRestoreStartupResult?> RecoverIncompleteAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var transientDirectory in _workspaceManager.EnumerateTransientDirectories().ToArray())
            {
                try
                {
                    _workspaceManager.Delete(transientDirectory);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "启动期间清理废弃的备份工作区失败。");
                }
            }

            BackupRestoreStartupResult? lastResult = null;
            foreach (var directory in _workspaceManager.EnumerateTransactionDirectories().ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                BackupRestoreTransaction transaction;
                try
                {
                    transaction = DataBackupService.ReadTransaction(directory);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "在受保护工作区中检测到无效的备份恢复事务。");
                    var invalidId = Path.GetFileName(directory);
                    if (Guid.TryParseExact(invalidId, "N", out _))
                    {
                        try
                        {
                            await backupSecretStore.ClearRestoreSecretAsync(invalidId, CancellationToken.None);
                        }
                        catch (Exception cleanupException)
                        {
                            logger.LogWarning(
                                cleanupException,
                                "清理无效恢复事务的密钥失败。事务 ID={TransactionId}。",
                                invalidId);
                            continue;
                        }
                    }

                    _workspaceManager.Delete(directory);
                    continue;
                }

                if (transaction.Phase is BackupRestoreTransactionPhase.SyncStatePending or
                    BackupRestoreTransactionPhase.Committed)
                {
                    await FinalizeSyncStateAsync(transaction, cancellationToken);
                    if (transaction.Phase != BackupRestoreTransactionPhase.Committed)
                    {
                        transaction = transaction with { Phase = BackupRestoreTransactionPhase.Committed };
                        await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);
                    }

                    lastResult = new BackupRestoreStartupResult(
                        true,
                        "备份数据已完整恢复，数据库和安全凭据均已更新",
                        transaction.TransactionId);
                    await SaveResultAsync(lastResult, cancellationToken);
                    await CleanupTransactionAsync(directory, transaction.TransactionId);
                    continue;
                }

                if (transaction.Phase == BackupRestoreTransactionPhase.RolledBack)
                {
                    await CleanupTransactionAsync(directory, transaction.TransactionId);
                    continue;
                }

                if (transaction.Phase is BackupRestoreTransactionPhase.Prepared or
                    BackupRestoreTransactionPhase.RollbackCreated)
                {
                    lastResult = new BackupRestoreStartupResult(
                        false,
                        "上次数据恢复在覆盖本地数据前中断，现有数据保持不变",
                        transaction.TransactionId);
                    await SaveResultAsync(lastResult, cancellationToken);
                    await CleanupTransactionAsync(directory, transaction.TransactionId);
                    continue;
                }

                try
                {
                    await RollbackAsync(directory, transaction, cancellationToken);
                    lastResult = new BackupRestoreStartupResult(
                        false,
                        "检测到未完成的数据恢复，已自动恢复覆盖前的数据",
                        transaction.TransactionId);
                }
                catch (Exception ex)
                {
                    logger.LogCritical(
                        ex,
                        "备份恢复的故障恢复失败。事务 ID={TransactionId}。",
                        transaction.TransactionId);
                    throw new InvalidOperationException("未完成的数据恢复无法自动回滚，请保留数据目录并联系维护者", ex);
                }

                await SaveResultAsync(lastResult, cancellationToken);
                await CleanupTransactionAsync(directory, transaction.TransactionId);
            }

            return lastResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupRestoreStartupResult> ApplyAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new ArgumentException("恢复事务标识无效", nameof(transactionId));
        }

        await _gate.WaitAsync(cancellationToken);
        var directory = _workspaceManager.GetTransactionDirectory(transactionId);
        BackupRestoreTransaction? transaction = null;
        try
        {
            transaction = DataBackupService.ReadTransaction(directory);
            if (transaction.TransactionId != transactionId ||
                transaction.Phase != BackupRestoreTransactionPhase.Prepared)
            {
                throw new InvalidDataException("恢复事务状态无效");
            }

            var secretEnvelope = await LoadSecretEnvelopeAsync(transactionId, cancellationToken);
            var incoming = Path.Combine(directory, transaction.IncomingFileName);
            var restoredDatabase = Path.Combine(directory, "restored.db");
            var contents = await _codec.ReadAsync(
                incoming,
                secretEnvelope.IncomingPassword,
                restoredDatabase,
                cancellationToken);
            try
            {
                var manifest = JsonSerializer.Deserialize<BackupManifest>(contents.Manifest, AppJson.Default)
                               ?? throw new InvalidDataException("备份清单为空");
                var restoredSecrets = JsonSerializer.Deserialize<BackupSecrets>(contents.Secrets, AppJson.Default)
                                      ?? throw new InvalidDataException("备份安全凭据为空");
                await ValidateIncomingAsync(restoredDatabase, contents.Secrets, manifest, restoredSecrets, cancellationToken);

                var currentSemanticFingerprint = await CreateRollbackArchiveAsync(
                    directory,
                    secretEnvelope.IncomingPassword,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(transaction.ExpectedLocalSemanticFingerprint) &&
                    !string.Equals(
                        transaction.ExpectedLocalSemanticFingerprint,
                        currentSemanticFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "本地数据在恢复事务创建后发生变化，已取消本次恢复，请重新检查差异");
                }

                transaction = transaction with { Phase = BackupRestoreTransactionPhase.RollbackCreated };
                await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);

                transaction = transaction with { Phase = BackupRestoreTransactionPhase.DatabaseInstalled };
                await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);
                InstallDatabase(directory, restoredDatabase);

                await ApplySecretsAsync(restoredSecrets, cancellationToken);
                await backupSecretStore.SaveBackupPasswordAsync(
                    secretEnvelope.IncomingPassword,
                    cancellationToken);
                transaction = transaction with
                {
                    Phase = BackupRestoreTransactionPhase.CredentialsInstalled,
                    SemanticFingerprint = manifest.SemanticFingerprint
                };
                await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);

                StorageDatabaseValidator.ValidateBackup(connectionFactory.DatabasePath);
                var result = new BackupRestoreStartupResult(
                    true,
                    "备份数据已安装并通过校验，等待应用初始化后提交",
                    transactionId);
                logger.LogInformation(
                    "备份恢复已安装，正在等待启动提交。事务 ID={TransactionId}，来源={Source}，数据库字节数={DatabaseBytes}。",
                    transactionId,
                    transaction.Source,
                    manifest.DatabaseLength);
                return result;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(contents.Secrets);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "备份恢复失败。事务 ID={TransactionId}。", transactionId);
            if (transaction is not null && transaction.Phase >= BackupRestoreTransactionPhase.DatabaseInstalled)
            {
                try
                {
                    await RollbackAsync(directory, transaction, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogCritical(
                        rollbackException,
                        "备份恢复回滚失败。事务 ID={TransactionId}。",
                        transactionId);
                    throw new InvalidOperationException("数据恢复失败且自动回滚失败，请保留数据目录并联系维护者", new AggregateException(ex, rollbackException));
                }
            }

            var result = new BackupRestoreStartupResult(
                false,
                $"数据恢复失败，已保留或恢复原有数据：{ex.Message}",
                transactionId);
            await SaveResultAsync(result, CancellationToken.None);
            activityLogService.Write(LogEntryKind.Error, "Backup", result.Message);
            await CleanupTransactionAsync(directory, transactionId);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupRestoreStartupResult> CompleteAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new ArgumentException("恢复事务标识无效", nameof(transactionId));
        }

        await _gate.WaitAsync(cancellationToken);
        var directory = _workspaceManager.GetTransactionDirectory(transactionId);
        try
        {
            var transaction = DataBackupService.ReadTransaction(directory);
            if (transaction.TransactionId != transactionId ||
                transaction.Phase != BackupRestoreTransactionPhase.CredentialsInstalled ||
                string.IsNullOrWhiteSpace(transaction.SemanticFingerprint))
            {
                throw new InvalidDataException("恢复事务尚未达到可提交状态");
            }

            StorageDatabaseValidator.ValidateBackup(connectionFactory.DatabasePath);
            transaction = transaction with { Phase = BackupRestoreTransactionPhase.SyncStatePending };
            await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);
            await FinalizeSyncStateAsync(transaction, cancellationToken);
            transaction = transaction with { Phase = BackupRestoreTransactionPhase.Committed };
            await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);

            var result = new BackupRestoreStartupResult(
                true,
                "备份数据已完整恢复，数据库和安全凭据均已更新",
                transactionId);
            try
            {
                await SaveResultAsync(result, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "持久化已完成的恢复结果失败。事务 ID={TransactionId}。", transactionId);
            }
            logger.LogInformation(
                "应用成功启动后，备份恢复已提交。事务 ID={TransactionId}，来源={Source}。",
                transactionId,
                transaction.Source);
            activityLogService.Write(LogEntryKind.Success, "Backup", result.Message);
            await CleanupTransactionAsync(directory, transactionId);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BackupRestoreStartupResult?> ConsumeStartupResultAsync(
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_workspaceManager.Root, ResultFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<BackupRestoreStartupResult>(json, AppJson.Default);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private async Task<string> CreateRollbackArchiveAsync(
        string directory,
        string password,
        CancellationToken cancellationToken)
    {
        var snapshot = Path.Combine(directory, "rollback-source.db");
        await CreateDatabaseSnapshotAsync(snapshot, cancellationToken);
        var secrets = await LoadCurrentSecretsAsync(cancellationToken);
        var secretBytes = JsonSerializer.SerializeToUtf8Bytes(secrets, AppJson.Default);
        try
        {
            var inventory = await BackupInventoryReader.ReadAsync(snapshot, secrets, cancellationToken);
            var databaseHash = await HashFileAsync(snapshot, cancellationToken);
            var secretHash = Convert.ToHexString(SHA256.HashData(secretBytes));
            var manifest = new BackupManifest(
                EncryptedBackupArchiveCodec.FormatVersion,
                appVersionProvider.CurrentVersionText,
                AppDatabaseSchema.CurrentVersion,
                timeProvider.GetUtcNow(),
                OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "other",
                new FileInfo(snapshot).Length,
                databaseHash,
                secretBytes.LongLength,
                secretHash,
                inventory.Fingerprint,
                inventory.Summary);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, AppJson.Default);
            var sources = new BackupArchiveSource[]
            {
                new(
                    EncryptedBackupArchiveCodec.ManifestEntryName,
                    manifestBytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(manifestBytes)),
                    Content: manifestBytes),
                new(
                    EncryptedBackupArchiveCodec.DatabaseEntryName,
                    manifest.DatabaseLength,
                    databaseHash,
                    FilePath: snapshot),
                new(
                    EncryptedBackupArchiveCodec.SecretsEntryName,
                    secretBytes.LongLength,
                    secretHash,
                    Content: secretBytes)
            };
            await _codec.WriteAsync(
                Path.Combine(directory, "rollback.igobackup"),
                password,
                sources,
                cancellationToken);
            return inventory.Fingerprint;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
            TryDelete(snapshot);
        }
    }

    private async Task RollbackAsync(
        string directory,
        BackupRestoreTransaction transaction,
        CancellationToken cancellationToken)
    {
        var envelope = await LoadSecretEnvelopeAsync(transaction.TransactionId, cancellationToken);
        var rollbackDatabase = Path.Combine(directory, "rollback.db");
        TryDelete(rollbackDatabase);
        var contents = await _codec.ReadAsync(
            Path.Combine(directory, "rollback.igobackup"),
            envelope.IncomingPassword,
            rollbackDatabase,
            cancellationToken);
        try
        {
            var secrets = JsonSerializer.Deserialize<BackupSecrets>(contents.Secrets, AppJson.Default)
                          ?? throw new InvalidDataException("回滚安全凭据为空");
            StorageDatabaseValidator.ValidateBackup(rollbackDatabase);
            ReplaceDatabaseWith(rollbackDatabase);
            await ApplySecretsAsync(secrets, cancellationToken);
            if (envelope.PreviousBackupPassword is null)
            {
                await backupSecretStore.ClearBackupPasswordAsync(cancellationToken);
            }
            else
            {
                await backupSecretStore.SaveBackupPasswordAsync(
                    envelope.PreviousBackupPassword,
                    cancellationToken);
            }

            transaction = transaction with { Phase = BackupRestoreTransactionPhase.RolledBack };
            await DataBackupService.WriteTransactionAsync(directory, transaction, cancellationToken);
            logger.LogWarning("备份恢复已回滚。事务 ID={TransactionId}。", transaction.TransactionId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents.Secrets);
        }
    }

    private async Task FinalizeSyncStateAsync(
        BackupRestoreTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.Source == BackupRestoreSource.WebDav)
        {
            try
            {
                await webDavSyncService.RecordRestoredBaselineAsync(
                    transaction.SemanticFingerprint!,
                    new WebDavRemoteMetadata(
                        true,
                        transaction.RemoteContentLength,
                        transaction.RemoteETag,
                        transaction.RemoteLastModified),
                    transaction.RemoteEndpointFingerprint
                    ?? throw new InvalidDataException("云恢复事务缺少端点指纹"),
                    transaction.RemoteFileSha256
                    ?? throw new InvalidDataException("云恢复事务缺少远端文件哈希"),
                    cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                changeTracker.MarkChanged(
                    pauseAutomaticUpload: true,
                    "云恢复已完成，但同步基线保存失败；请手动确认下一次上传");
                logger.LogWarning(
                    ex,
                    "恢复后建立 WebDAV 基线失败。事务 ID={TransactionId}。",
                    transaction.TransactionId);
                return;
            }
        }

        changeTracker.MarkChanged(
            pauseAutomaticUpload: true,
            "已从本地备份导入数据；自动上传暂停，请手动确认是否覆盖远端");
    }

    private void InstallDatabase(string directory, string restoredDatabase)
    {
        SqliteConnection.ClearAllPools();
        var databasePath = connectionFactory.DatabasePath;
        var oldDirectory = Path.Combine(directory, "old-database");
        Directory.CreateDirectory(oldDirectory);
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var source = databasePath + suffix;
            if (File.Exists(source))
            {
                File.Move(source, Path.Combine(oldDirectory, Path.GetFileName(source)), overwrite: false);
            }
        }

        File.Move(restoredDatabase, databasePath, overwrite: false);
    }

    private void ReplaceDatabaseWith(string restoredDatabase)
    {
        SqliteConnection.ClearAllPools();
        var databasePath = connectionFactory.DatabasePath;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            TryDelete(databasePath + suffix);
        }

        File.Move(restoredDatabase, databasePath, overwrite: false);
    }

    private async Task ApplySecretsAsync(BackupSecrets secrets, CancellationToken cancellationToken)
    {
        if (secrets.Session is null)
        {
            await credentialStore.ClearSessionAsync(cancellationToken);
        }
        else
        {
            await credentialStore.SaveSessionAsync(secrets.Session, cancellationToken);
        }

        if (secrets.RemoteCheckInSession is null)
        {
            await credentialStore.ClearRemoteCheckInSessionAsync(cancellationToken);
        }
        else
        {
            await credentialStore.SaveRemoteCheckInSessionAsync(secrets.RemoteCheckInSession, cancellationToken);
        }

        if (string.IsNullOrEmpty(secrets.WebDavPassword))
        {
            await backupSecretStore.ClearWebDavPasswordAsync(cancellationToken);
        }
        else
        {
            await backupSecretStore.SaveWebDavPasswordAsync(secrets.WebDavPassword, cancellationToken);
        }
    }

    private async Task<BackupSecrets> LoadCurrentSecretsAsync(CancellationToken cancellationToken)
        => new(
            await credentialStore.LoadSessionAsync(cancellationToken),
            await credentialStore.LoadRemoteCheckInSessionAsync(cancellationToken),
            await backupSecretStore.LoadWebDavPasswordAsync(cancellationToken));

    private async Task<BackupRestoreSecretEnvelope> LoadSecretEnvelopeAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var value = await backupSecretStore.LoadRestoreSecretAsync(transactionId, cancellationToken)
                    ?? throw new InvalidOperationException("恢复事务的安全密钥不存在");
        return JsonSerializer.Deserialize<BackupRestoreSecretEnvelope>(value, AppJson.Default)
               ?? throw new InvalidDataException("恢复事务的安全密钥格式无效");
    }

    private async Task ValidateIncomingAsync(
        string databasePath,
        byte[] secretBytes,
        BackupManifest manifest,
        BackupSecrets secrets,
        CancellationToken cancellationToken)
    {
        if (manifest.FormatVersion != EncryptedBackupArchiveCodec.FormatVersion ||
            manifest.DatabaseSchemaVersion > AppDatabaseSchema.CurrentVersion ||
            manifest.DatabaseLength != new FileInfo(databasePath).Length ||
            manifest.SecretsLength != secretBytes.LongLength ||
            !string.Equals(manifest.DatabaseSha256, await HashFileAsync(databasePath, cancellationToken), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.SecretsSha256, Convert.ToHexString(SHA256.HashData(secretBytes)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复前的备份清单校验失败");
        }

        StorageDatabaseValidator.ValidateBackup(databasePath);
        var inventory = await BackupInventoryReader.ReadAsync(databasePath, secrets, cancellationToken);
        if (inventory.Summary != manifest.Summary ||
            !string.Equals(inventory.Fingerprint, manifest.SemanticFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复前的数据摘要校验失败");
        }
    }

    private async Task CreateDatabaseSnapshotAsync(string destination, CancellationToken cancellationToken)
    {
        await using var source = connectionFactory.Create();
        await source.OpenAsync(cancellationToken);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = destination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        await using var target = new SqliteConnection(builder.ToString());
        await target.OpenAsync(cancellationToken);
        await Task.Run(() => source.BackupDatabase(target), cancellationToken);
    }

    private async Task SaveResultAsync(
        BackupRestoreStartupResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_workspaceManager.Root);
        var path = Path.Combine(_workspaceManager.Root, ResultFileName);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(result, AppJson.Default),
            new System.Text.UTF8Encoding(false),
            cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private async Task CleanupTransactionAsync(string directory, string transactionId)
    {
        try
        {
            await backupSecretStore.ClearRestoreSecretAsync(transactionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理恢复事务密钥失败。事务 ID={TransactionId}。", transactionId);
            logger.LogWarning(
                "已保留恢复事务目录，以便重试清理密钥。事务 ID={TransactionId}。",
                transactionId);
            return;
        }

        try
        {
            _workspaceManager.Delete(directory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理恢复事务失败。事务 ID={TransactionId}。", transactionId);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
