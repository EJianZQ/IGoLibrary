using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GlobalLeakCoordinator : IGlobalLeakCoordinator
{
    private readonly GlobalLeakWorkflowRunner _workflowRunner;
    private readonly CoordinatorRunController _controller;

    public GlobalLeakCoordinator(
        GlobalLeakWorkflowRunner workflowRunner,
        ICoordinatorRuntime runtime)
    {
        _workflowRunner = workflowRunner;
        _controller = new CoordinatorRunController("全域捡漏", runtime);
    }

    public event EventHandler<CoordinatorStatus>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    public CoordinatorStatus GetStatus() => _controller.GetStatus();

    public Task StartAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default)
    {
        return _controller.StartAsync(
            (context, token) => _workflowRunner.RunAsync(plan, context, token),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _controller.StopAsync(cancellationToken);
    }
}
