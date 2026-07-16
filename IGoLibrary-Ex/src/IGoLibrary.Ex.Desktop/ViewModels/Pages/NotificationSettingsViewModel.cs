using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class NotificationSettingsViewModel(
    ISettingsWorkflowService settingsWorkflowService,
    INotificationTestService notificationTestService,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IErrorDialogService errorDialogService,
    TimeProvider? timeProvider = null) : ViewModelBase
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default)
    {
        return settingsWorkflowService.SaveNotificationSettingsAsync(alerts, cancellationToken);
    }

    public Task SendTestEmailAsync(
        EmailAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestEmailAsync(settings, cancellationToken);
    }

    public Task SendTestTelegramAsync(
        TelegramAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestTelegramAsync(settings, cancellationToken);
    }

    public Task SendTestBarkAsync(
        BarkAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestBarkAsync(settings, cancellationToken);
    }

    public Task SendTestWxPusherAsync(
        WxPusherAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestWxPusherAsync(settings, cancellationToken);
    }

    public Task SendTestServerChanAsync(
        ServerChanAlertChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestServerChanAsync(settings, cancellationToken);
    }

    public Task SendTestLocalAlertAsync(
        LocalDesktopAlertSettings settings,
        CancellationToken cancellationToken = default)
    {
        return notificationTestService.SendTestLocalAlertAsync(settings, cancellationToken);
    }
}
