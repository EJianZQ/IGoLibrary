using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GrabSeatCoordinator : IGrabSeatCoordinator
{
    private readonly GrabSeatWorkflowRunner _stateMachine;
    private readonly CoordinatorRunController _controller;

    public GrabSeatCoordinator(
        GrabSeatWorkflowRunner stateMachine,
        ICoordinatorRuntime runtime)
    {
        _stateMachine = stateMachine;
        _controller = new CoordinatorRunController("抢座", runtime);
    }

    public event EventHandler<CoordinatorStatus>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    public CoordinatorStatus GetStatus() => _controller.GetStatus();

    public Task StartAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default)
    {
        return _controller.StartAsync(
            (context, token) => _stateMachine.RunAsync(plan, context, token),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _controller.StopAsync(cancellationToken);
    }

    internal static DateTimeOffset ResolveNextScheduledStart(TimeOnly scheduledStart, DateTimeOffset now)
    {
        return GrabSeatStateMachine.ResolveNextScheduledStart(scheduledStart, now);
    }
}
