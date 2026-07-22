using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class UpdateInstallGuard(
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator) : IUpdateInstallGuard
{
    public IReadOnlyList<string> GetBlockingTaskNames()
    {
        var blockingTasks = new List<string>(4);
        AddIfActive(blockingTasks, "抢座", grabSeatCoordinator.GetStatus());
        AddIfActive(blockingTasks, "全域捡漏", globalLeakCoordinator.GetStatus());
        AddIfActive(blockingTasks, "占座", occupySeatCoordinator.GetStatus());
        AddIfActive(blockingTasks, "明日预约", tomorrowReservationCoordinator.GetStatus());
        return blockingTasks;
    }

    private static void AddIfActive(
        ICollection<string> names,
        string displayName,
        CoordinatorStatus status)
    {
        if (status.IsActive)
        {
            names.Add(displayName);
        }
    }
}
