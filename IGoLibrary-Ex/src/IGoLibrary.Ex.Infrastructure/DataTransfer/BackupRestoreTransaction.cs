using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal enum BackupRestoreTransactionPhase
{
    Prepared = 0,
    RollbackCreated = 1,
    DatabaseInstalled = 2,
    CredentialsInstalled = 3,
    Committed = 4,
    RolledBack = 5,
    SyncStatePending = 6
}

internal sealed record BackupRestoreTransaction(
    string TransactionId,
    BackupRestoreSource Source,
    BackupRestoreTransactionPhase Phase,
    string IncomingFileName,
    string? RemoteETag,
    DateTimeOffset? RemoteLastModified,
    long? RemoteContentLength,
    string? RemoteEndpointFingerprint,
    string? RemoteFileSha256,
    DateTimeOffset CreatedAtUtc,
    string? ExpectedLocalSemanticFingerprint = null,
    string? SemanticFingerprint = null,
    string? FailureMessage = null);

internal sealed record BackupRestoreSecretEnvelope(
    string IncomingPassword,
    string? PreviousBackupPassword);
