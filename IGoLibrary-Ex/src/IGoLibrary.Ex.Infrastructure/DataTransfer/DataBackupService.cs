using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

public sealed class DataBackupService(
    SqliteConnectionFactory connectionFactory,
    StorageLocations locations,
    ICredentialStore credentialStore,
    IBackupSecretStore backupSecretStore,
    IPersistentDataChangeTracker changeTracker,
    IAppVersionProvider appVersionProvider,
    IActivityLogService activityLogService,
    ILogger<DataBackupService> logger,
    TimeProvider timeProvider) : IDataBackupService
{
    private static readonly TimeSpan PreviewMaximumAge = TimeSpan.FromHours(24);
    private readonly EncryptedBackupArchiveCodec _codec = new();
    private readonly BackupWorkspaceManager _workspaceManager = new(locations);
    private readonly ConcurrentDictionary<string, PreparedState> _prepared = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public async Task<BackupExportResult> ExportAsync(
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        BackupPasswordRules.Validate(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var operationId = Guid.NewGuid().ToString("N");
        var startedAt = timeProvider.GetUtcNow();
        await _operationGate.WaitAsync(cancellationToken);
        string? workspace = null;
        string? temporaryOutput = null;
        byte[]? secretsBytes = null;
        try
        {
            activityLogService.Write(LogEntryKind.Info, "Backup", "正在导出全部应用数据");
        logger.LogInformation("备份导出已开始。操作 ID={OperationId}。", operationId);
            workspace = _workspaceManager.Create("export", operationId);
            var snapshotPath = Path.Combine(workspace, EncryptedBackupArchiveCodec.DatabaseEntryName);
            await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
            StorageDatabaseValidator.ValidateBackup(snapshotPath);

            var secrets = await LoadSecretsAsync(cancellationToken);
            secretsBytes = JsonSerializer.SerializeToUtf8Bytes(secrets, AppJson.Default);
            var inventory = await BackupInventoryReader.ReadAsync(snapshotPath, secrets, cancellationToken);
            var databaseHash = await ComputeFileHashAsync(snapshotPath, cancellationToken);
            var secretsHash = Convert.ToHexString(SHA256.HashData(secretsBytes));
            var databaseLength = new FileInfo(snapshotPath).Length;
            var manifest = new BackupManifest(
                EncryptedBackupArchiveCodec.FormatVersion,
                appVersionProvider.CurrentVersionText,
                AppDatabaseSchema.CurrentVersion,
                timeProvider.GetUtcNow(),
                GetPlatformName(),
                databaseLength,
                databaseHash,
                secretsBytes.LongLength,
                secretsHash,
                inventory.Fingerprint,
                inventory.Summary);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, AppJson.Default);

            var fullDestination = Path.GetFullPath(destinationPath);
            var destinationDirectory = Path.GetDirectoryName(fullDestination)
                                       ?? throw new InvalidOperationException("无法确定备份文件目录");
            Directory.CreateDirectory(destinationDirectory);
            temporaryOutput = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(fullDestination)}.{operationId}.tmp");
            var sources = new BackupArchiveSource[]
            {
                new(
                    EncryptedBackupArchiveCodec.ManifestEntryName,
                    manifestBytes.LongLength,
                    Convert.ToHexString(SHA256.HashData(manifestBytes)),
                    Content: manifestBytes),
                new(
                    EncryptedBackupArchiveCodec.DatabaseEntryName,
                    databaseLength,
                    databaseHash,
                    FilePath: snapshotPath),
                new(
                    EncryptedBackupArchiveCodec.SecretsEntryName,
                    secretsBytes.LongLength,
                    secretsHash,
                    Content: secretsBytes)
            };
            await _codec.WriteAsync(temporaryOutput, password, sources, cancellationToken);
            File.Move(temporaryOutput, fullDestination, overwrite: true);
            temporaryOutput = null;

            var fileSize = new FileInfo(fullDestination).Length;
            logger.LogInformation(
                "备份导出已完成。操作 ID={OperationId}，字节数={Bytes}，耗时毫秒={DurationMs}，收藏数={Favorites}，标签数={Labels}，历史记录数={History}。",
                operationId,
                fileSize,
                (timeProvider.GetUtcNow() - startedAt).TotalMilliseconds,
                manifest.Summary.FavoriteCount,
                manifest.Summary.SeatLabelCount,
                manifest.Summary.TaskHistoryCount);
            activityLogService.Write(LogEntryKind.Success, "Backup", "全部应用数据已成功导出");
            return new BackupExportResult(fullDestination, fileSize, manifest, operationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("备份导出已取消。操作 ID={OperationId}。", operationId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "备份导出失败。操作 ID={OperationId}。", operationId);
            activityLogService.Write(LogEntryKind.Error, "Backup", $"导出应用数据失败：{ex.Message}", ex);
            throw;
        }
        finally
        {
            if (secretsBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretsBytes);
            }

            TryDeleteFile(temporaryOutput);
            TryDeleteWorkspace(workspace);
            _operationGate.Release();
        }
    }

    public async Task<PreparedBackup> PrepareImportAsync(
        string sourcePath,
        string password,
        CancellationToken cancellationToken = default)
    {
        BackupPasswordRules.Validate(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var operationId = Guid.NewGuid().ToString("N");
        var preparationId = Guid.NewGuid().ToString("N");
        await _operationGate.WaitAsync(cancellationToken);
        string? workspace = null;
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceWorkspace = GetOwnedWebDavWorkspace(fullSourcePath);
        byte[]? secretBytes = null;
        try
        {
            _workspaceManager.CleanupPreviews(PreviewMaximumAge, timeProvider.GetUtcNow());
            workspace = _workspaceManager.Create("preview", preparationId);
            var preparedArchive = Path.Combine(workspace, "prepared.igobackup");
            await CopyFileAsync(fullSourcePath, preparedArchive, cancellationToken);
            var preparedArchiveSha256 = await ComputeFileHashAsync(preparedArchive, cancellationToken);
            var importedDatabase = Path.Combine(workspace, "imported.db");
            logger.LogInformation(
            "备份导入准备已开始。操作 ID={OperationId}，准备 ID={PreparationId}。",
                operationId,
                preparationId);
            var contents = await _codec.ReadAsync(
                preparedArchive,
                password,
                importedDatabase,
                cancellationToken);
            secretBytes = contents.Secrets;
            var manifest = DeserializeManifest(contents.Manifest);
            if (secretBytes.LongLength != manifest.SecretsLength ||
                !string.Equals(
                    Convert.ToHexString(SHA256.HashData(secretBytes)),
                    manifest.SecretsSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("备份安全凭据长度或哈希与清单不一致");
            }

            var secrets = DeserializeSecrets(secretBytes);
            await ValidatePreparedDataAsync(importedDatabase, manifest, secrets, cancellationToken);

            var currentState = await CaptureCurrentStateAsync(
                Path.Combine(workspace, "current.db"),
                cancellationToken);
            var backupInventory = await BackupInventoryReader.ReadAsync(
                importedDatabase,
                secrets,
                cancellationToken);
            var comparison = BackupInventoryReader.Compare(currentState.Inventory, backupInventory);
            var state = new PreparedState(
                preparationId,
                fullSourcePath,
                preparedArchive,
                preparedArchiveSha256,
                workspace,
                sourceWorkspace,
                manifest,
                comparison,
                currentState.Inventory.Fingerprint,
                currentState.Version,
                operationId);
            if (!_prepared.TryAdd(preparationId, state))
            {
                throw new InvalidOperationException("无法登记备份预览状态");
            }

            workspace = null;
            logger.LogInformation(
                "备份导入准备已完成。操作 ID={OperationId}，新增数={Added}，删除数={Removed}，变更数={Changed}，未变更数={Unchanged}。",
                operationId,
                comparison.AddedCount,
                comparison.RemovedCount,
                comparison.ChangedCount,
                comparison.UnchangedCount);
            return new PreparedBackup(
                preparationId,
                state.SourcePath,
                manifest,
                comparison,
                operationId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "备份导入准备失败。操作 ID={OperationId}，准备 ID={PreparationId}。",
                operationId,
                preparationId);
            TryDeleteWorkspace(workspace);
            TryDeleteWorkspace(sourceWorkspace);
            throw;
        }
        finally
        {
            if (secretBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }

            _operationGate.Release();
        }
    }

    public async Task<string> StageRestoreAsync(
        BackupRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        BackupPasswordRules.Validate(request.Password);
        await _operationGate.WaitAsync(cancellationToken);
        var transactionId = Guid.NewGuid().ToString("N");
        string? transactionDirectory = null;
        try
        {
            if (!_prepared.TryGetValue(request.PreparationId, out var prepared))
            {
                throw new InvalidOperationException("备份预览已失效，请重新选择并检查备份文件");
            }

            var currentState = await CaptureCurrentStateAsync(
                Path.Combine(prepared.Workspace, "stage-current.db"),
                cancellationToken);
            if (currentState.Version != prepared.CurrentVersion ||
                !string.Equals(
                    currentState.Inventory.Fingerprint,
                    prepared.CurrentSemanticFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("本地数据自备份预览后已发生变化，请重新检查恢复差异");
            }

            transactionDirectory = _workspaceManager.Create("restore", transactionId);
            var incomingPath = Path.Combine(transactionDirectory, "incoming.igobackup");
            await CopyFileAsync(prepared.PreparedArchivePath, incomingPath, cancellationToken);
            if (!string.Equals(
                    await ComputeFileHashAsync(incomingPath, cancellationToken),
                    prepared.PreparedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("备份预览归档已发生变化，请重新选择并检查备份文件");
            }

            var transaction = new BackupRestoreTransaction(
                transactionId,
                request.Source,
                BackupRestoreTransactionPhase.Prepared,
                Path.GetFileName(incomingPath),
                request.RemoteETag,
                request.RemoteLastModified,
                request.RemoteContentLength,
                request.RemoteEndpointFingerprint,
                request.RemoteFileSha256,
                timeProvider.GetUtcNow(),
                ExpectedLocalSemanticFingerprint: currentState.Inventory.Fingerprint,
                SemanticFingerprint: null);
            await WriteTransactionAsync(transactionDirectory, transaction, cancellationToken);
            var restoreSecrets = new BackupRestoreSecretEnvelope(
                request.Password,
                await backupSecretStore.LoadBackupPasswordAsync(cancellationToken));
            await backupSecretStore.SaveRestoreSecretAsync(
                transactionId,
                JsonSerializer.Serialize(restoreSecrets, AppJson.Default),
                cancellationToken);
            _prepared.TryRemove(request.PreparationId, out _);
            TryDeleteWorkspace(prepared.Workspace);
            TryDeleteWorkspace(prepared.SourceWorkspace);
            logger.LogInformation(
            "备份恢复已暂存。事务 ID={TransactionId}，来源={Source}。",
                transactionId,
                request.Source);
            return transactionId;
        }
        catch (Exception stageException)
        {
            try
            {
                await backupSecretStore.ClearRestoreSecretAsync(transactionId, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(
                    cleanupException,
                    "恢复暂存失败且无法清理其密钥；已保留事务目录。事务 ID={TransactionId}。",
                    transactionId);
                throw new AggregateException(
                    "恢复事务创建失败，并且无法清理临时安全凭据；已保留事务目录供下次启动重试",
                    stageException,
                    cleanupException);
            }

            TryDeleteWorkspace(transactionDirectory);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DiscardPreparedAsync(
        string preparationId,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            if (_prepared.TryRemove(preparationId, out var state))
            {
                TryDeleteWorkspace(state.Workspace);
                TryDeleteWorkspace(state.SourceWorkspace);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal static async Task WriteTransactionAsync(
        string transactionDirectory,
        BackupRestoreTransaction transaction,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(transactionDirectory, "transaction.json");
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(transaction, AppJson.Default);
        await File.WriteAllTextAsync(temporaryPath, json, new System.Text.UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    internal static BackupRestoreTransaction ReadTransaction(string transactionDirectory)
    {
        var json = File.ReadAllText(Path.Combine(transactionDirectory, "transaction.json"));
        return JsonSerializer.Deserialize<BackupRestoreTransaction>(json, AppJson.Default)
               ?? throw new InvalidDataException("恢复事务文件为空");
    }

    private async Task CreateDatabaseSnapshotAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var source = connectionFactory.Create();
        await source.OpenAsync(cancellationToken);
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        await using var destination = new SqliteConnection(destinationBuilder.ToString());
        await destination.OpenAsync(cancellationToken);
        await Task.Run(() => source.BackupDatabase(destination), cancellationToken);
    }

    private async Task<BackupSecrets> LoadSecretsAsync(CancellationToken cancellationToken)
    {
        var session = await credentialStore.LoadSessionAsync(cancellationToken);
        var remoteCheckIn = await credentialStore.LoadRemoteCheckInSessionAsync(cancellationToken);
        var webDavPassword = await backupSecretStore.LoadWebDavPasswordAsync(cancellationToken);
        return new BackupSecrets(session, remoteCheckIn, webDavPassword);
    }

    private async Task<CurrentState> CaptureCurrentStateAsync(
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var version = changeTracker.Version;
        TryDeleteFile(snapshotPath);
        try
        {
            await CreateDatabaseSnapshotAsync(snapshotPath, cancellationToken);
            var secrets = await LoadSecretsAsync(cancellationToken);
            var inventory = await BackupInventoryReader.ReadAsync(
                snapshotPath,
                secrets,
                cancellationToken);
            if (changeTracker.Version != version)
            {
                throw new InvalidOperationException("读取本地数据期间检测到并发修改，请稍后重新预览备份");
            }

            return new CurrentState(inventory, version);
        }
        finally
        {
            TryDeleteFile(snapshotPath);
        }
    }

    private static BackupManifest DeserializeManifest(byte[] bytes)
    {
        var manifest = JsonSerializer.Deserialize<BackupManifest>(bytes, AppJson.Default)
                       ?? throw new InvalidDataException("备份清单为空");
        if (manifest.FormatVersion != EncryptedBackupArchiveCodec.FormatVersion ||
            manifest.DatabaseSchemaVersion > AppDatabaseSchema.CurrentVersion ||
            manifest.DatabaseLength <= 0 ||
            manifest.SecretsLength < 0)
        {
            throw new InvalidDataException("备份清单版本或长度无效");
        }

        return manifest;
    }

    private static BackupSecrets DeserializeSecrets(byte[] bytes)
        => JsonSerializer.Deserialize<BackupSecrets>(bytes, AppJson.Default)
           ?? throw new InvalidDataException("备份安全凭据清单为空");

    private static async Task ValidatePreparedDataAsync(
        string databasePath,
        BackupManifest manifest,
        BackupSecrets secrets,
        CancellationToken cancellationToken)
    {
        StorageDatabaseValidator.ValidateBackup(databasePath);
        var databaseInfo = new FileInfo(databasePath);
        if (databaseInfo.Length != manifest.DatabaseLength ||
            !string.Equals(
                await ComputeFileHashAsync(databasePath, cancellationToken),
                manifest.DatabaseSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("备份数据库长度或哈希与清单不一致");
        }

        var inventory = await BackupInventoryReader.ReadAsync(databasePath, secrets, cancellationToken);
        if (inventory.Summary != manifest.Summary ||
            !string.Equals(inventory.Fingerprint, manifest.SemanticFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("备份数据摘要与清单不一致");
        }
    }

    private static async Task<string> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken)
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

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static string GetPlatformName()
        => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "other";

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
            logger.LogWarning(ex, "清理备份工作区失败。路径哈希={PathHash}。", HashPath(workspace));
        }
    }

    private void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清理备份临时文件失败。");
        }
    }

    private static string HashPath(string path)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..12];

    private string? GetOwnedWebDavWorkspace(string sourcePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(sourcePath);
            var workspace = Directory.GetParent(fullPath)?.FullName;
            var category = workspace is null ? null : Directory.GetParent(workspace)?.FullName;
            if (workspace is null || category is null ||
                !string.Equals(
                    Path.TrimEndingDirectorySeparator(category),
                    Path.TrimEndingDirectorySeparator(Path.Combine(_workspaceManager.Root, "webdav")),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !Guid.TryParseExact(Path.GetFileName(workspace), "N", out _) ||
                !string.Equals(Path.GetFileName(fullPath), "download.igobackup", StringComparison.Ordinal))
            {
                return null;
            }

            return workspace;
        }
        catch
        {
            return null;
        }
    }

    private sealed record PreparedState(
        string Id,
        string SourcePath,
        string PreparedArchivePath,
        string PreparedArchiveSha256,
        string Workspace,
        string? SourceWorkspace,
        BackupManifest Manifest,
        BackupComparison Comparison,
        string CurrentSemanticFingerprint,
        long CurrentVersion,
        string OperationId);

    private sealed record CurrentState(BackupInventory Inventory, long Version);
}
