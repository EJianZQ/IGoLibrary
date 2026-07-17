using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskLaunchHistoryRepository
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

    Task<TaskLaunchHistorySaveResult> SaveGrabAsync(
        GrabTaskLaunchRecord record,
        string fingerprint,
        CancellationToken cancellationToken = default);

    Task<TaskLaunchHistorySaveResult> SaveGlobalLeakAsync(
        GlobalLeakTaskLaunchRecord record,
        string fingerprint,
        CancellationToken cancellationToken = default);
}
