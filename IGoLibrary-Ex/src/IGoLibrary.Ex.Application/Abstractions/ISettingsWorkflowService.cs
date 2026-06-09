using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISettingsWorkflowService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task<AppSettings> SaveSystemSettingsAsync(
        SystemSettingsSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task SaveNotificationSettingsAsync(
        TaskEventAlertSettings alerts,
        CancellationToken cancellationToken = default);

    Task SaveGrabReservationStrategyAsync(
        GrabReservationStrategy strategy,
        CancellationToken cancellationToken = default);

    Task SaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default);

    Task SaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default);

    Task ClearStoredLibrarySelectionAsync(CancellationToken cancellationToken = default);

    Task SaveDashboardMetricsAsync(
        DashboardMetrics metrics,
        CancellationToken cancellationToken = default);
}
