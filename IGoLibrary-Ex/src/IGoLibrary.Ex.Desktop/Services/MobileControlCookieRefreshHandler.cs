using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlCookieRefreshHandler(
    SessionViewModel sessionViewModel) : IMobileControlCookieRefreshHandler
{
    public async Task<SessionCookieLinkParseResult> RefreshCookieFromLinkAsync(
        string linkText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await sessionViewModel.ParseCookieFromLinkAsync(
                linkText,
                SessionCookieLinkParseOptions.MobileControlRefresh);
        }

        var completion = new TaskCompletionSource<SessionCookieLinkParseResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await sessionViewModel.ParseCookieFromLinkAsync(
                    linkText,
                    SessionCookieLinkParseOptions.MobileControlRefresh));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return await completion.Task;
    }
}
