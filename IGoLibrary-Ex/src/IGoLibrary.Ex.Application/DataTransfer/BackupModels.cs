using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Backup;

public enum BackupRestoreSource
{
    LocalFile = 0,
    WebDav = 1
}

public enum BackupDifferenceKind
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,
    Changed = 3
}

public sealed record BackupDataSummary(
    int SettingsCount,
    int FavoriteCount,
    int SeatLabelCount,
    int ProtocolOverrideCount,
    int TaskHistoryCount,
    bool HasSession,
    bool HasRemoteCheckInSession,
    bool HasWebDavPassword);

public sealed record BackupManifest(
    int FormatVersion,
    string AppVersion,
    int DatabaseSchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string SourcePlatform,
    long DatabaseLength,
    string DatabaseSha256,
    long SecretsLength,
    string SecretsSha256,
    string SemanticFingerprint,
    BackupDataSummary Summary);

public sealed record BackupSecrets(
    SessionCredentials? Session,
    RemoteCheckInSessionCredentials? RemoteCheckInSession,
    string? WebDavPassword);

public sealed record BackupDifferenceItem(
    string Category,
    BackupDifferenceKind Kind,
    string LocalSummary,
    string BackupSummary,
    bool IsSensitive = false,
    int AddedCount = 0,
    int RemovedCount = 0,
    int ChangedCount = 0,
    int UnchangedCount = 0,
    IReadOnlyList<BackupDifferenceDetail>? Details = null);

public sealed record BackupDifferenceDetail(
    BackupDifferenceKind Kind,
    string Key,
    string LocalValue,
    string BackupValue,
    bool IsSensitive = false);

public sealed record BackupComparison(
    int AddedCount,
    int RemovedCount,
    int ChangedCount,
    int UnchangedCount,
    IReadOnlyList<BackupDifferenceItem> Items);

public sealed record BackupExportResult(
    string FilePath,
    long FileSize,
    BackupManifest Manifest,
    string OperationId);

public sealed record PreparedBackup(
    string PreparationId,
    string SourceFilePath,
    BackupManifest Manifest,
    BackupComparison Comparison,
    string OperationId);

public sealed record BackupRestoreRequest(
    string PreparationId,
    string Password,
    BackupRestoreSource Source,
    string? RemoteETag = null,
    DateTimeOffset? RemoteLastModified = null,
    long? RemoteContentLength = null,
    string? RemoteEndpointFingerprint = null,
    string? RemoteFileSha256 = null);

public sealed record BackupRestoreStartupResult(
    bool Succeeded,
    string Message,
    string? TransactionId = null);

public sealed record WebDavRemoteMetadata(
    bool Exists,
    long? ContentLength,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed record WebDavUploadResult(
    WebDavRemoteMetadata Metadata,
    BackupManifest Manifest,
    string OperationId);

public sealed record WebDavDownloadResult(
    string LocalFilePath,
    WebDavRemoteMetadata Metadata,
    string EndpointFingerprint,
    string FileSha256,
    string OperationId);

public sealed record BackupSyncRuntimeStatus(
    bool IsBusy,
    bool HasConflict,
    string Message,
    DateTimeOffset? LastSuccessfulSync,
    WebDavRemoteMetadata? RemoteMetadata)
{
    public static BackupSyncRuntimeStatus Idle { get; } = new(
        false,
        false,
        "尚未同步",
        null,
        null);
}
