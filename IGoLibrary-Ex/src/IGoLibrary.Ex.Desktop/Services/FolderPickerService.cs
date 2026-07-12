using Avalonia.Platform.Storage;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class FolderPickerService(AppWindowService appWindowService) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow
                    ?? throw new InvalidOperationException("主窗口尚未就绪，无法选择文件夹");
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
