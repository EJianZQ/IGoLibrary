using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IGrabSeatCoordinator
{
    event EventHandler<CoordinatorStatus>? StatusChanged;

    Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    CoordinatorStatus GetStatus();
}
