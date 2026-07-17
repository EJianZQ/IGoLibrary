using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskLaunchHistoryService
{
    Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(
        CancellationToken cancellationToken = default);

    Task<GrabTaskLaunchRecord?> GetGrabAsync(
        string recordId,
        CancellationToken cancellationToken = default);

    Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(
        string recordId,
        CancellationToken cancellationToken = default);

    Task<TaskLaunchHistorySaveResult> RecordGrabAsync(
        GrabSeatPlan plan,
        CancellationToken cancellationToken = default);

    Task<TaskLaunchHistorySaveResult> RecordGlobalLeakAsync(
        GlobalLeakPlan plan,
        CancellationToken cancellationToken = default);
}
