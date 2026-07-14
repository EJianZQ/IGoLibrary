using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IWindowsUpdateProgressDialogService
{
    Task<WindowsPortableUpdateResult> ShowAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default);
}
