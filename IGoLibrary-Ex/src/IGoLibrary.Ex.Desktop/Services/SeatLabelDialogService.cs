using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class SeatLabelDialogService(AppWindowService appWindowService) : ISeatLabelDialogService
{
    public async Task<string?> ShowAsync(
        SeatLabelDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = appWindowService.MainWindow;
        if (owner is null)
        {
            return null;
        }

        var dialog = new SeatLabelEditorWindow(new SeatLabelEditorViewModel(request));
        return await dialog.ShowDialog<string?>(owner);
    }
}
