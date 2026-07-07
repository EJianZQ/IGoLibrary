using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlCookieRefreshHandler
{
    Task<SessionCookieLinkParseResult> RefreshCookieFromLinkAsync(
        string linkText,
        CancellationToken cancellationToken = default);
}
