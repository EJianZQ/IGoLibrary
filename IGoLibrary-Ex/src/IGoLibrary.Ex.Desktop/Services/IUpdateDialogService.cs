using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IUpdateDialogService
{
    Task<UpdateDialogResult> ShowUpdateAsync(
        ReleaseUpdateInfo release,
        CancellationToken cancellationToken = default);
}

public enum UpdateDialogResult
{
    Later,
    OpenReleasePage,
    SkipVersion
}
