using Avalonia.Threading;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class ErrorDialogService(AppWindowService appWindowService) : IErrorDialogService
{
    public async Task ShowErrorAsync(string title, string errorType, string errorMessage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
        {
            await ShowCoreAsync(title, errorType, errorMessage, cancellationToken);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await ShowCoreAsync(title, errorType, errorMessage, cancellationToken);
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        await completion.Task;
    }

    private async Task ShowCoreAsync(string title, string errorType, string errorMessage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new ErrorDetailsWindow(title, errorType, errorMessage);
        if (appWindowService.MainWindow is { } owner)
        {
            await dialog.ShowDialog(owner);
            return;
        }

        dialog.Show();
    }
}
