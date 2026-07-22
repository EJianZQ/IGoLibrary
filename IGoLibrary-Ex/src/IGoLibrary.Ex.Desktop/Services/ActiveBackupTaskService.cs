using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class ActiveBackupTaskService(
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator) : IActiveBackupTaskService
{
    public IReadOnlyList<string> GetActiveTaskNames()
        => GetActiveTasks().Select(static task => task.Name).ToArray();

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var active = GetActiveTasks();
        await Task.WhenAll(active.Select(task => task.Stop(cancellationToken)));
    }

    private List<ActiveTask> GetActiveTasks()
    {
        var tasks = new List<ActiveTask>();
        AddIfActive(tasks, "抢座", grabSeatCoordinator.GetStatus(), grabSeatCoordinator.StopAsync);
        AddIfActive(tasks, "全域捡漏", globalLeakCoordinator.GetStatus(), globalLeakCoordinator.StopAsync);
        AddIfActive(tasks, "占座", occupySeatCoordinator.GetStatus(), occupySeatCoordinator.StopAsync);
        AddIfActive(tasks, "明日预约", tomorrowReservationCoordinator.GetStatus(), tomorrowReservationCoordinator.StopAsync);
        return tasks;
    }

    private static void AddIfActive(
        ICollection<ActiveTask> tasks,
        string name,
        CoordinatorStatus status,
        Func<CancellationToken, Task> stop)
    {
        if (status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping)
        {
            tasks.Add(new ActiveTask(name, stop));
        }
    }

    private sealed record ActiveTask(string Name, Func<CancellationToken, Task> Stop);
}
