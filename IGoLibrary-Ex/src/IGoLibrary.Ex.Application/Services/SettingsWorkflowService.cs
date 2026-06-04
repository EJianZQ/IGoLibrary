using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SettingsWorkflowService(ISettingsService settingsService) : ISettingsWorkflowService
{
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return settingsService.LoadAsync(cancellationToken);
    }

    public async Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        var settings = current with
        {
            Notifications = current.Notifications with
            {
                AppBannerNotificationsEnabled = snapshot.AppBannerNotificationsEnabled,
                TaskEventAlerts = snapshot.TaskEventAlerts
            },
            Ui = current.Ui with
            {
                MinimizeToTray = snapshot.MinimizeToTray,
                Theme = snapshot.Theme
            },
            Protocol = current.Protocol with
            {
                TemplateOverridesEnabled = snapshot.ProtocolTemplateOverridesEnabled
            },
            RequestPolicy = new RequestPolicySettings(
                Math.Max(3, snapshot.RequestTimeoutSeconds),
                Math.Max(1, snapshot.RequestRetryCount)),
            Tasks = current.Tasks with
            {
                GrabReservationStrategy = snapshot.GrabReservationStrategy
            }
        };

        await settingsService.SaveAsync(settings, cancellationToken);
        return settings;
    }

    public async Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        await settingsService.SaveAsync(current with
        {
            Notifications = current.Notifications with
            {
                TaskEventAlerts = alerts
            }
        }, cancellationToken);
    }

    public async Task SaveGrabReservationStrategyAsync(
        GrabReservationStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        if (current.Tasks.GrabReservationStrategy == strategy)
        {
            return;
        }

        await settingsService.SaveAsync(current with
        {
            Tasks = current.Tasks with
            {
                GrabReservationStrategy = strategy
            }
        }, cancellationToken);
    }

    public async Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        if (current.Venue.LastLibraryId is null && string.IsNullOrWhiteSpace(current.Venue.LastLibraryName))
        {
            return;
        }

        await settingsService.SaveAsync(current with
        {
            Venue = VenueSelectionSettings.Default
        }, cancellationToken);
    }

    public async Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        var current = await settingsService.LoadAsync(cancellationToken);
        if (current.Dashboard == metrics)
        {
            return;
        }

        await settingsService.SaveAsync(current with
        {
            Dashboard = metrics
        }, cancellationToken);
    }
}
