namespace IGoLibrary.Ex.Desktop.Services;

public interface IErrorDialogService
{
    Task ShowErrorAsync(string title, string errorType, string errorMessage, CancellationToken cancellationToken = default);
}
