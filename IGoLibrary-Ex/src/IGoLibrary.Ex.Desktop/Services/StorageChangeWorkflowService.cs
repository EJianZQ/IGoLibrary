using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class StorageChangeWorkflowService(
    IStorageLocationService storageLocationService,
    IStorageChangeDialogService dialogService,
    IApplicationRestartService restartService,
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    IActivityLogService activityLogService) : IStorageChangeWorkflowService
{
    public async Task<bool> ApplyAsync(StorageLocations target, CancellationToken cancellationToken = default)
    {
        await storageLocationService.ValidateAsync(target, cancellationToken);
        var current = storageLocationService.Current;
        var dataChanged = !PathsEqual(current.DataDirectory, target.DataDirectory);
        var logsChanged = !PathsEqual(current.LogDirectory, target.LogDirectory);
        if (!dataChanged && !logsChanged)
        {
            return false;
        }

        var migrationDecision = await dialogService.ConfirmMigrationAsync(
            current,
            target,
            dataChanged,
            logsChanged,
            cancellationToken);
        if (migrationDecision == StorageMigrationDecision.Cancel)
        {
            return false;
        }

        var overwriteTargetDatabase = false;
        if (dataChanged)
        {
            var inspection = await storageLocationService.InspectTargetDatabaseAsync(
                target.DataDirectory,
                cancellationToken);
            if (inspection.Exists)
            {
                var databasePath = Path.Combine(target.DataDirectory, "igolibrary-ex.db");
                if (migrationDecision == StorageMigrationDecision.Migrate)
                {
                    overwriteTargetDatabase = await dialogService.ConfirmOverwriteDatabaseAsync(
                        databasePath,
                        cancellationToken);
                    if (!overwriteTargetDatabase)
                    {
                        return false;
                    }
                }
                else
                {
                    if (!inspection.IsValid)
                    {
                        throw new InvalidDataException(
                            $"目标目录中的现有数据库无效，无法直接使用：{inspection.FailureMessage ?? "未知错误"}");
                    }

                    if (!await dialogService.ConfirmUseExistingDatabaseAsync(
                            databasePath,
                            cancellationToken))
                    {
                        return false;
                    }
                }
            }
        }

        var activeTasks = GetActiveTasks();
        if (activeTasks.Count > 0)
        {
            if (!await dialogService.ConfirmStopTasksAsync(
                    activeTasks.Select(task => task.Name).ToArray(),
                    cancellationToken))
            {
                return false;
            }

            await Task.WhenAll(activeTasks.Select(task => task.Stop(cancellationToken)));
        }

        var migrate = migrationDecision == StorageMigrationDecision.Migrate;
        await storageLocationService.StageChangeAsync(
            new StorageLocationChangeRequest(
                target,
                MigrateData: migrate && dataChanged,
                MigrateLogs: migrate && logsChanged,
                OverwriteTargetDatabase: overwriteTargetDatabase),
            cancellationToken);
        try
        {
            activityLogService.Write(LogEntryKind.Info, "Storage", "已暂存存储位置更改，正在重启应用。");
            await restartService.RestartAsync(cancellationToken);
            return true;
        }
        catch
        {
            await storageLocationService.CancelPendingChangeAsync(CancellationToken.None);
            throw;
        }
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

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record ActiveTask(string Name, Func<CancellationToken, Task> Stop);
}
