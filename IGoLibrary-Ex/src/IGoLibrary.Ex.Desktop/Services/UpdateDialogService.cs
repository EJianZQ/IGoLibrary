using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class UpdateDialogService(
    AppWindowService appWindowService,
    IAppVersionProvider appVersionProvider) : IUpdateDialogService
{
    public async Task<UpdateDialogResult> ShowUpdateAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ShowCoreAsync(release, cancellationToken);
        }

        var completion = new TaskCompletionSource<UpdateDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var result = await ShowCoreAsync(release, cancellationToken);
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task;
    }

    private async Task<UpdateDialogResult> ShowCoreAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new UpdateReleaseWindow(release, appVersionProvider.CurrentVersion.ToString());
        if (appWindowService.MainWindow is { } owner)
        {
            return await dialog.ShowDialog<UpdateDialogResult>(owner);
        }

        dialog.Show();
        return UpdateDialogResult.Later;
    }
}
