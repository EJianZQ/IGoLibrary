namespace IGoLibrary.Ex.Desktop.Services;

public interface IExternalLinkService
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}
