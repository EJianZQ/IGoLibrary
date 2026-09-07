using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed class GlobalLeakCoordinator : IGlobalLeakCoordinator
{
    private readonly GlobalLeakWorkflowRunner _workflowRunner;
    private readonly CoordinatorRunController _controller;
    private readonly GlobalLeakConfigurationGate _configurationGate;

    public GlobalLeakCoordinator(
        GlobalLeakWorkflowRunner workflowRunner,
        ICoordinatorRuntime runtime,
        GlobalLeakConfigurationGate configurationGate,
        IAppLogWriter? logWriter = null)
    {
        _workflowRunner = workflowRunner;
        _configurationGate = configurationGate;
        _controller = new CoordinatorRunController("全域捡漏", runtime, logWriter);
    }

    public event EventHandler<CoordinatorStatus>? StatusChanged
    {
        add => _controller.StatusChanged += value;
        remove => _controller.StatusChanged -= value;
    }

    public CoordinatorStatus GetStatus() => _controller.GetStatus();

    public async Task StartAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var snapshot = plan with { Libraries = plan.Libraries.ToArray() };
        await _configurationGate.EnterAsync(cancellationToken);
        try
        {
            await _controller.StartAsync(
                (context, token) => _workflowRunner.RunAsync(snapshot, context, token),
                cancellationToken);
        }
        finally { _configurationGate.Exit(); }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _controller.StopAsync(cancellationToken);
    }
}
