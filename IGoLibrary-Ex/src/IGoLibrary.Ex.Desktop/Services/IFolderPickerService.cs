namespace IGoLibrary.Ex.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default);
}
