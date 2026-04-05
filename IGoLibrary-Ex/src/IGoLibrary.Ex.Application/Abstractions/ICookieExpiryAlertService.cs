using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ICookieExpiryAlertService
{
    Task NotifyCookieExpiredAsync(string source, string reason, CancellationToken cancellationToken = default);

    Task SendTestEmailAsync(CookieExpiryEmailAlertSettings settings, CancellationToken cancellationToken = default);

    Task SendTestLocalAlertAsync(CookieExpiryLocalAlertSettings settings, CancellationToken cancellationToken = default);
}
