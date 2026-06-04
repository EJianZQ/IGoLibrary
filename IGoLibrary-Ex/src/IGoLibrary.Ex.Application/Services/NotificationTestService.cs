using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class NotificationTestService(
    ITaskEventAlertService taskEventAlertService) : INotificationTestService
{
    public Task SendTestEmailAsync(
        EmailAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return taskEventAlertService.SendTestEmailAsync(settings, cancellationToken);
    }

    public Task SendTestTelegramAsync(
        TelegramAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return taskEventAlertService.SendTestTelegramAsync(settings, cancellationToken);
    }

    public Task SendTestLocalAlertAsync(
        LocalAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return taskEventAlertService.SendTestLocalAlertAsync(settings, cancellationToken);
    }
}
