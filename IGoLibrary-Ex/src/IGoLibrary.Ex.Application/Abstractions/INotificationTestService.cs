using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface INotificationTestService
{
    Task SendTestEmailAsync(EmailAlertChannelSettings settings, CancellationToken cancellationToken = default);

    Task SendTestTelegramAsync(TelegramAlertChannelSettings settings, CancellationToken cancellationToken = default);

    Task SendTestBarkAsync(BarkAlertChannelSettings settings, CancellationToken cancellationToken = default);

    Task SendTestLocalAlertAsync(LocalDesktopAlertSettings settings, CancellationToken cancellationToken = default);
}
