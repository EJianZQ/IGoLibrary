using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITaskLaunchService
{
    Task StartGrabAsync(
        GrabSeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default);

    Task StartGlobalLeakAsync(
        GlobalLeakPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default);

    Task StartOccupyAsync(
        OccupySeatPlan plan,
        TaskLaunchSource source,
        CancellationToken cancellationToken = default);
}
