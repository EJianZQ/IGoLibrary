using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskAlertService
{
    Task NotifyCookieExpiredAsync(string source, string reason, CancellationToken cancellationToken = default);

    Task NotifyGrabSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default);

    Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default);

    Task SendTestEmailAsync(CookieExpiryEmailAlertSettings settings, CancellationToken cancellationToken = default);

    Task SendTestLocalAlertAsync(CookieExpiryLocalAlertSettings settings, CancellationToken cancellationToken = default);
}
