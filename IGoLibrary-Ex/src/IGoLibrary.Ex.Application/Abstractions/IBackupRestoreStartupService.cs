using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IBackupRestoreStartupService
{
    Task<BackupRestoreStartupResult?> RecoverIncompleteAsync(
        CancellationToken cancellationToken = default);

    Task<BackupRestoreStartupResult> ApplyAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    Task<BackupRestoreStartupResult> CompleteAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    Task<BackupRestoreStartupResult?> ConsumeStartupResultAsync(
        CancellationToken cancellationToken = default);
}
