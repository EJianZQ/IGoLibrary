using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IGlobalLeakCoordinator
{
    event EventHandler<CoordinatorStatus>? StatusChanged;

    Task StartAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    CoordinatorStatus GetStatus();
}
