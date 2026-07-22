namespace IGoLibrary.Ex.Desktop.Services;

public interface IBackupFilePickerService
{
    Task<string?> PickExportPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);

    Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default);
}
