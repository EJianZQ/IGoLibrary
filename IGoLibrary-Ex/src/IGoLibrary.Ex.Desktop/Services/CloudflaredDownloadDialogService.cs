namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class CloudflaredDownloadDialogService(
    AppWindowService appWindowService,
    ICloudflaredInstallService installService,
    ICloudflaredPathProvider paths) : ICloudflaredDownloadDialogService
{
    public async Task<bool> ConfirmDownloadAsync(
        CloudflaredAssetDescriptor asset,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return false;
        }

        var installDirectory = paths.GetManagedInstallDirectory(asset);
        var dialog = new CloudflaredDownloadConfirmationWindow(asset, installDirectory);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<CloudflaredInstallDialogResult> ShowInstallAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return new CloudflaredInstallDialogResult(
                CloudflaredInstallDialogOutcome.Canceled,
                "主窗口不可用，未开始下载");
        }

        var dialog = new CloudflaredDownloadProgressWindow(installService, cancellationToken);
        return await dialog.ShowDialog<CloudflaredInstallDialogResult>(owner);
    }
}
