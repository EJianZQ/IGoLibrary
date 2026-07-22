using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IDataBackupService
{
    Task<BackupExportResult> ExportAsync(
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default);

    Task<PreparedBackup> PrepareImportAsync(
        string sourcePath,
        string password,
        CancellationToken cancellationToken = default);

    Task<string> StageRestoreAsync(
        BackupRestoreRequest request,
        CancellationToken cancellationToken = default);

    Task DiscardPreparedAsync(
        string preparationId,
        CancellationToken cancellationToken = default);
}
