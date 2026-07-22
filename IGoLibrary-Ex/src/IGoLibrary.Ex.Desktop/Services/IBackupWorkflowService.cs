namespace IGoLibrary.Ex.Desktop.Services;

public interface IBackupWorkflowService
{
    Task<bool> ExportLocalAsync(CancellationToken cancellationToken = default);

    Task<bool> ImportLocalAsync(CancellationToken cancellationToken = default);

    Task<bool> DownloadAndRestoreAsync(CancellationToken cancellationToken = default);

    Task<bool> UploadAsync(CancellationToken cancellationToken = default);

    Task<bool> ChangeBackupPasswordAsync(CancellationToken cancellationToken = default);
}
