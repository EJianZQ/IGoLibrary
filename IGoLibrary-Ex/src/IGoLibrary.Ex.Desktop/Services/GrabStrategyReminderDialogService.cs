namespace IGoLibrary.Ex.Desktop.Services;

public sealed class GrabStrategyReminderDialogService(AppWindowService appWindowService)
    : IGrabStrategyReminderDialogService
{
    public async Task<GrabStrategyReminderResult> ShowAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return GrabStrategyReminderResult.Cancelled;
        }

        var dialog = new GrabStrategyReminderWindow();
        return await dialog.ShowDialog<GrabStrategyReminderResult?>(owner)
               ?? GrabStrategyReminderResult.Cancelled;
    }
}
