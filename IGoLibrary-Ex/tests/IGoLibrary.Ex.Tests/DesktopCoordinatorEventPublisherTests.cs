using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Tests;

public sealed class DesktopCoordinatorEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_MapsGrabSucceededEventToTaskEventAlertDispatcher()
    {
        var taskAlertService = new FakeTaskEventAlertDispatcher();
        var publisher = CreatePublisher(taskAlertService);

        await publisher.PublishAsync(new GrabSucceededCoordinatorEvent("自科阅览区一", "1号座"));

        Assert.Contains(
            taskAlertService.GrabSucceededNotifications,
            item => item.LibraryName == "自科阅览区一" && item.SeatName == "1号座");
    }

    [Fact]
    public async Task PublishAsync_MapsSessionInvalidEventToTaskEventAlertDispatcher()
    {
        var taskAlertService = new FakeTaskEventAlertDispatcher();
        var publisher = CreatePublisher(taskAlertService);

        await publisher.PublishAsync(new SessionInvalidCoordinatorEvent("抢座轮询", "Cookie 已过期。"));

        Assert.Contains(
            taskAlertService.SessionInvalidNotifications,
            item => item.Source == "抢座轮询" && item.Reason == "Cookie 已过期。");
    }

    [Fact]
    public async Task PublishAsync_MapsTaskFailedEventToTaskEventAlertDispatcher()
    {
        var taskAlertService = new FakeTaskEventAlertDispatcher();
        var publisher = CreatePublisher(taskAlertService);

        await publisher.PublishAsync(new TaskFailedCoordinatorEvent("占座", "预约状态获取失败"));

        Assert.Contains(
            taskAlertService.TaskFailedNotifications,
            item => item.TaskName == "占座" && item.Reason == "预约状态获取失败");
    }

    [Fact]
    public async Task PublishAsync_MapsOccupyReReserveSucceededEventToTaskEventAlertDispatcher()
    {
        var taskAlertService = new FakeTaskEventAlertDispatcher();
        var publisher = CreatePublisher(taskAlertService);

        await publisher.PublishAsync(new OccupyReReserveSucceededCoordinatorEvent("1号座"));

        Assert.Contains("1号座", taskAlertService.OccupyReReserveSucceededNotifications);
    }

    [Fact]
    public async Task PublishAsync_WritesWarning_WhenMappingFails()
    {
        var activityLogService = new ActivityLogService();
        var taskAlertService = new FakeTaskEventAlertDispatcher
        {
            NotifyTaskFailedException = new InvalidOperationException("发送失败")
        };
        var publisher = CreatePublisher(taskAlertService, activityLogService: activityLogService);

        await publisher.PublishAsync(new TaskFailedCoordinatorEvent("抢座", "场馆接口暂时不可用"));

        Assert.Contains(
            activityLogService.Entries,
            entry => entry.Kind == LogEntryKind.Warning
                && entry.Category == "Alert"
                && entry.Message.Contains("处理任务事件提醒失败"));
    }

    private static DesktopCoordinatorEventPublisher CreatePublisher(
        FakeTaskEventAlertDispatcher? taskAlertService = null,
        ActivityLogService? activityLogService = null)
    {
        return new DesktopCoordinatorEventPublisher(
            taskAlertService ?? new FakeTaskEventAlertDispatcher(),
            activityLogService ?? new ActivityLogService());
    }
}
