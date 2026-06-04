using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class DesktopCoordinatorEventPublisher(
    ITaskEventAlertService taskEventAlertService,
    INotificationService notificationService,
    IActivityLogService activityLogService) : ICoordinatorEventPublisher
{
    public async Task PublishAsync(CoordinatorEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (@event)
            {
                case GrabSucceededCoordinatorEvent grabSucceeded:
                    await taskEventAlertService.NotifyGrabSucceededAsync(
                        grabSucceeded.LibraryName,
                        grabSucceeded.SeatName,
                        cancellationToken);
                    break;
                case OccupyReReserveSucceededCoordinatorEvent occupySucceeded:
                    await notificationService.ShowSuccessAsync(
                        "占座成功",
                        $"{occupySucceeded.SeatName} 已重新预约。",
                        cancellationToken);
                    break;
                case SessionInvalidCoordinatorEvent sessionInvalid:
                    await taskEventAlertService.NotifySessionInvalidAsync(
                        sessionInvalid.Source,
                        sessionInvalid.Reason,
                        cancellationToken);
                    break;
                case TaskFailedCoordinatorEvent taskFailed:
                    await taskEventAlertService.NotifyTaskFailedAsync(
                        taskFailed.TaskName,
                        taskFailed.Reason,
                        cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"不支持的任务事件类型：{@event.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Warning, "Alert", $"处理任务事件提醒失败：{ex.Message}");
        }
    }
}
