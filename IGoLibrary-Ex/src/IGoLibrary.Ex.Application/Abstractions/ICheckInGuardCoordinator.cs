using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ICheckInGuardCoordinator
{
    event EventHandler<CoordinatorStatus>? StatusChanged;

    CoordinatorStatus GetStatus();

    Task StartAsync(CheckInGuardPlan plan, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
