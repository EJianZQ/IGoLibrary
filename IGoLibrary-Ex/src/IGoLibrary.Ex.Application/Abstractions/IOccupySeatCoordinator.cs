using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IOccupySeatCoordinator
{
    event EventHandler<CoordinatorStatus>? StatusChanged;

    Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    CoordinatorStatus GetStatus();
}
