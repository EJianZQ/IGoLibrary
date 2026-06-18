using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class VenueAvailabilityCoordinator : IVenueAvailabilityCoordinator
{
    private readonly VenueAvailabilityWorkflowRunner _stateMachine;
    private readonly CoordinatorRunController _controller;

    public VenueAvailabilityCoordinator(
        VenueAvailabilityWorkflowRunner stateMachine,
        ICoordinatorRuntime runtime)
    {
        _stateMachine = stateMachine;
        _controller = new CoordinatorRunController("空座追踪", runtime);
    }

    public event EventHandler<CoordinatorStatus>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    public CoordinatorStatus GetStatus() => _controller.GetStatus();

    public Task StartAsync(VenueAvailabilityWatchPlan plan, CancellationToken cancellationToken = default)
    {
        return _controller.StartAsync(
            (context, token) => _stateMachine.RunAsync(plan, context, token),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _controller.StopAsync(cancellationToken);
    }
}
