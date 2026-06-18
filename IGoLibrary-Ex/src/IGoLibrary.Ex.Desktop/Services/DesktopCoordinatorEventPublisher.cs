using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class DesktopCoordinatorEventPublisher(
    ITaskEventAlertDispatcher taskEventAlertDispatcher,
    IActivityLogService activityLogService) : ICoordinatorEventPublisher
{
    public async Task PublishAsync(CoordinatorEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (@event)
            {
                case GrabSucceededCoordinatorEvent grabSucceeded:
                    await taskEventAlertDispatcher.NotifyGrabSucceededAsync(
                        grabSucceeded.LibraryName,
                        grabSucceeded.SeatName,
                        cancellationToken);
                    break;
                case OccupyReReserveSucceededCoordinatorEvent occupySucceeded:
                    await taskEventAlertDispatcher.NotifyOccupyReReserveSucceededAsync(
                        occupySucceeded.SeatName,
                        cancellationToken);
                    break;
                case TomorrowReservationSucceededCoordinatorEvent tomorrowSucceeded:
                    await taskEventAlertDispatcher.NotifyTomorrowReservationSucceededAsync(
                        tomorrowSucceeded.LibraryName,
                        tomorrowSucceeded.SeatName,
                        tomorrowSucceeded.Day,
                        cancellationToken);
                    break;
                case VenueAvailableCoordinatorEvent venueAvailable:
                    await taskEventAlertDispatcher.NotifyVenueAvailableAsync(
                        venueAvailable.LibraryName,
                        venueAvailable.AvailableSeats,
                        cancellationToken);
                    break;
                case CheckInReminderCoordinatorEvent checkInReminder:
                    await taskEventAlertDispatcher.NotifyCheckInReminderAsync(
                        checkInReminder.LibraryName,
                        checkInReminder.SeatName,
                        checkInReminder.Deadline,
                        cancellationToken);
                    break;
                case CheckInMissedCoordinatorEvent checkInMissed:
                    await taskEventAlertDispatcher.NotifyCheckInMissedAsync(
                        checkInMissed.LibraryName,
                        checkInMissed.SeatName,
                        checkInMissed.Deadline,
                        checkInMissed.ActionText,
                        cancellationToken);
                    break;
                case CheckInAutoRescueSucceededCoordinatorEvent checkInRescueSucceeded:
                    await taskEventAlertDispatcher.NotifyCheckInAutoRescueSucceededAsync(
                        checkInRescueSucceeded.LibraryName,
                        checkInRescueSucceeded.SeatName,
                        cancellationToken);
                    break;
                case SessionInvalidCoordinatorEvent sessionInvalid:
                    await taskEventAlertDispatcher.NotifySessionInvalidAsync(
                        sessionInvalid.Source,
                        sessionInvalid.Reason,
                        cancellationToken);
                    break;
                case TaskFailedCoordinatorEvent taskFailed:
                    await taskEventAlertDispatcher.NotifyTaskFailedAsync(
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
