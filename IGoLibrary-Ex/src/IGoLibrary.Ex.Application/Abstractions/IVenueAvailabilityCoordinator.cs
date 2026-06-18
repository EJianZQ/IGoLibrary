using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IVenueAvailabilityCoordinator
{
    event EventHandler<CoordinatorStatus>? StatusChanged;

    CoordinatorStatus GetStatus();

    Task StartAsync(VenueAvailabilityWatchPlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
