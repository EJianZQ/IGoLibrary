using Avalonia.Platform.Storage;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class BackupFilePickerService(AppWindowService appWindowService) : IBackupFilePickerService
{
    private static readonly FilePickerFileType BackupFileType = new("IGoLibrary-Ex 数据备份")
    {
        Patterns = ["*.igobackup"],
        MimeTypes = ["application/octet-stream"]
    };

    public async Task<string?> PickExportPathAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow
                    ?? throw new InvalidOperationException("主窗口尚未就绪，无法选择备份文件");
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出全部应用数据",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "igobackup",
            FileTypeChoices = [BackupFileType],
            ShowOverwritePrompt = true
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow
                    ?? throw new InvalidOperationException("主窗口尚未就绪，无法选择备份文件");
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要导入的数据备份",
            AllowMultiple = false,
            FileTypeFilter = [BackupFileType]
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
