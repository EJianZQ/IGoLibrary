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
        return await settingsService.UpdateAsync(current => current with
        {
            Notifications = current.Notifications with
            {
                TaskEventAlerts = snapshot.TaskEventAlerts
            },
            Ui = current.Ui with
            {
                MinimizeToTray = snapshot.MinimizeToTray,
                Theme = snapshot.Theme
            },
            TraceIntProtocol = current.TraceIntProtocol with
            {
                GraphQlOverridesEnabled = snapshot.TraceIntGraphQlOverridesEnabled
            },
            Updates = (current.Updates ?? UpdateCheckSettings.Default) with
            {
                CheckOnStartup = snapshot.CheckUpdatesOnStartup
            },
            Network = new NetworkRequestSettings(
                Math.Max(3, snapshot.RequestTimeoutSeconds),
                Math.Max(0, snapshot.NetworkMaxRetries)),
            Tasks = current.Tasks with
            {
                Grab = current.Tasks.Grab with
                {
                    ReservationStrategy = snapshot.GrabReservationStrategy
                }
            }
        }, cancellationToken);
    }

    public async Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current => current with
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
        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.Grab.ReservationStrategy == strategy)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    Grab = current.Tasks.Grab with
                    {
                        ReservationStrategy = strategy
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        if (!IsTimeOfDay(value))
        {
            return;
        }

        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.Grab.DefaultScheduledStartTime == value)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    Grab = current.Tasks.Grab with
                    {
                        DefaultScheduledStartTime = value
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        if (!IsTimeOfDay(value))
        {
            return;
        }

        await settingsService.UpdateAsync(current =>
        {
            if (current.Tasks.TomorrowReservation.DefaultScheduledStartTime == value)
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    TomorrowReservation = current.Tasks.TomorrowReservation with
                    {
                        DefaultScheduledStartTime = value
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task SaveGlobalLeakSelectedLibrariesAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> libraries,
        CancellationToken cancellationToken = default)
    {
        var selectedLibraries = libraries
            .Select(static library => new GlobalLeakLibrarySelectionSettings(
                library.LibraryId,
                library.LibraryName,
                library.Floor))
            .ToArray();

        await settingsService.UpdateAsync(current =>
        {
            if (AreGlobalLeakSelectionsEqual(current.Tasks.GlobalLeak.SelectedLibraries, selectedLibraries))
            {
                return current;
            }

            return current with
            {
                Tasks = current.Tasks with
                {
                    GlobalLeak = current.Tasks.GlobalLeak with
                    {
                        SelectedLibraries = selectedLibraries
                    }
                }
            };
        }, cancellationToken);
    }

    public async Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            if (current.Venue.LastLibraryId is null && string.IsNullOrWhiteSpace(current.Venue.LastLibraryName))
            {
                return current;
            }

            return current with
            {
                Venue = VenueSelectionSettings.Default
            };
        }, cancellationToken);
    }

    public async Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        await settingsService.UpdateAsync(current =>
        {
            if (current.Dashboard == metrics)
            {
                return current;
            }

            return current with
            {
                Dashboard = metrics
            };
        }, cancellationToken);
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private static bool AreGlobalLeakSelectionsEqual(
        IReadOnlyList<GlobalLeakLibrarySelectionSettings> current,
        IReadOnlyList<GlobalLeakLibrarySelectionSettings> updated)
    {
        if (current.Count != updated.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index] != updated[index])
            {
                return false;
            }
        }

        return true;
    }
}
