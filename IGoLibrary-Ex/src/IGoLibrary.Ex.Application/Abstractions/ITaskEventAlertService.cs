using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskEventAlertService
{
    Task NotifySessionInvalidAsync(string source, string reason, CancellationToken cancellationToken = default);

    Task NotifyGrabSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default);

    Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default);

    Task SendTestEmailAsync(EmailAlertChannelSettings settings, CancellationToken cancellationToken = default);

    Task SendTestTelegramAsync(TelegramAlertChannelSettings settings, CancellationToken cancellationToken = default);

    Task SendTestLocalAlertAsync(LocalAlertChannelSettings settings, CancellationToken cancellationToken = default);
}
