using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class TomorrowReservationCoordinator : ITomorrowReservationCoordinator
{
    private readonly TomorrowReservationWorkflowRunner _workflowRunner;
    private readonly CoordinatorRunController _controller;

    public TomorrowReservationCoordinator(
        TomorrowReservationWorkflowRunner workflowRunner,
        ICoordinatorRuntime runtime)
    {
        _workflowRunner = workflowRunner;
        _controller = new CoordinatorRunController("明日预约", runtime);
    }

    public event EventHandler<CoordinatorStatus>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    public CoordinatorStatus GetStatus() => _controller.GetStatus();

    public Task StartAsync(TomorrowReservationPlan plan, CancellationToken cancellationToken = default)
    {
        return _controller.StartAsync(
            (context, token) => _workflowRunner.RunAsync(plan, context, token),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _controller.StopAsync(cancellationToken);
    }

    internal static DateTimeOffset ResolveNextScheduledStart(TimeOnly scheduledStart, DateTimeOffset now)
    {
        return TomorrowReservationStateMachine.ResolveNextScheduledStart(scheduledStart, now);
    }
}
