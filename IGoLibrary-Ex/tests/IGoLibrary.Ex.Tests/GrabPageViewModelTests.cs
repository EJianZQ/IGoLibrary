using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabPageViewModelTests
{
    [Fact]
    public async Task StartGrabAsync_SwitchesAndPersistsOptimalStrategy_WhenUserAcceptsReminder()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(GrabStrategyReminderDecision.SwitchToOptimal));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Dialog.ShowCount);
        Assert.Equal((int)GrabReservationStrategy.QueryThenReserve, context.ViewModel.SelectedGrabReservationStrategyIndex);
        Assert.True(context.Settings.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.Equal(
            GrabReservationStrategy.QueryThenReserve,
            context.Settings.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.NotNull(context.Coordinator.LastPlan);
    }

    [Fact]
    public async Task StartGrabAsync_KeepsAndPersistsDirectStrategy_WhenUserDeclinesSwitch()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(GrabStrategyReminderDecision.KeepCurrent));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Dialog.ShowCount);
        Assert.Equal((int)GrabReservationStrategy.ReserveDirectly, context.ViewModel.SelectedGrabReservationStrategyIndex);
        Assert.Equal(
            GrabReservationStrategy.ReserveDirectly,
            context.Settings.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.NotNull(context.Coordinator.LastPlan);
    }

    [Theory]
    [InlineData(GrabStrategyReminderDecision.KeepCurrent, GrabReservationStrategy.ReserveDirectly)]
    [InlineData(GrabStrategyReminderDecision.SwitchToOptimal, GrabReservationStrategy.QueryThenReserve)]
    public async Task StartGrabAsync_DisablesReminderForEitherStartChoice_WhenCheckboxSelected(
        GrabStrategyReminderDecision decision,
        GrabReservationStrategy expectedStrategy)
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(decision, disableReminder: true));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Dialog.ShowCount);
        Assert.False(context.Settings.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.Equal(expectedStrategy, context.Settings.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.Equal((int)expectedStrategy, context.ViewModel.SelectedGrabReservationStrategyIndex);
        Assert.Equal(1, context.Settings.SaveCalls);
        Assert.NotNull(context.Coordinator.LastPlan);
    }

    [Fact]
    public async Task StartGrabAsync_DoesNotPersistOrStart_WhenReminderIsCancelled()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(GrabStrategyReminderDecision.Cancel));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(1, context.Dialog.ShowCount);
        Assert.Equal(0, context.Settings.SaveCalls);
        Assert.Null(context.Coordinator.LastPlan);
    }

    [Theory]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    public async Task StartGrabAsync_RemindsOnlyWhenSeatCountExceedsFive(
        int seatCount,
        int expectedShowCount)
    {
        var context = CreateContext(seatCount, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(GrabStrategyReminderDecision.KeepCurrent));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(expectedShowCount, context.Dialog.ShowCount);
        Assert.NotNull(context.Coordinator.LastPlan);
    }

    [Theory]
    [InlineData(false, GrabReservationStrategy.ReserveDirectly)]
    [InlineData(true, GrabReservationStrategy.QueryThenReserve)]
    public async Task StartGrabAsync_DoesNotRemind_WhenReminderDisabledOrStrategyAlreadyOptimal(
        bool reminderEnabled,
        GrabReservationStrategy strategy)
    {
        var context = CreateContext(6, reminderEnabled, strategy);

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Dialog.ShowCount);
        Assert.NotNull(context.Coordinator.LastPlan);
    }

    [Fact]
    public async Task StartGrabAsync_ValidatesScheduledTimeBeforeShowingReminder()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.ViewModel.IsGrabScheduledStartEnabled = true;
        context.ViewModel.ScheduledStartTime = TimeSpan.FromDays(1);

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal(0, context.Dialog.ShowCount);
        Assert.Null(context.Coordinator.LastPlan);
        Assert.Contains(context.Notifications.Warnings, item => item.Title == "启动抢座失败");
    }

    [Fact]
    public async Task StartGrabAsync_DoesNotStart_WhenStrategyPersistenceFails()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(GrabStrategyReminderDecision.SwitchToOptimal));
        context.Settings.UpdateExceptions.Enqueue(new InvalidOperationException("保存失败"));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Equal((int)GrabReservationStrategy.ReserveDirectly, context.ViewModel.SelectedGrabReservationStrategyIndex);
        Assert.Null(context.Coordinator.LastPlan);
        Assert.Contains(context.Notifications.Warnings, item => item.Message.Contains("保存失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGrabAsync_DoesNotStart_WhenReminderFails()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.ShowException = new InvalidOperationException("弹窗失败");

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Null(context.Coordinator.LastPlan);
        Assert.Contains(context.Notifications.Warnings, item => item.Message.Contains("弹窗失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGrabAsync_LeavesBothPreferencesUnchanged_WhenAtomicPersistenceFails()
    {
        var context = CreateContext(6, reminderEnabled: true, GrabReservationStrategy.ReserveDirectly);
        context.Dialog.Results.Enqueue(Result(
            GrabStrategyReminderDecision.KeepCurrent,
            disableReminder: true));
        context.Settings.UpdateExceptions.Enqueue(new InvalidOperationException("关闭提醒失败"));

        await context.ViewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.True(context.Settings.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.Equal(
            GrabReservationStrategy.ReserveDirectly,
            context.Settings.CurrentSettings.Tasks.Grab.ReservationStrategy);
        Assert.Equal((int)GrabReservationStrategy.ReserveDirectly, context.ViewModel.SelectedGrabReservationStrategyIndex);
        Assert.Equal(0, context.Settings.SaveCalls);
        Assert.Null(context.Coordinator.LastPlan);
        Assert.Contains(
            context.Notifications.Warnings,
            item => item.Message.Contains("关闭提醒失败", StringComparison.Ordinal));
    }

    private static TestContext CreateContext(
        int seatCount,
        bool reminderEnabled,
        GrabReservationStrategy strategy)
    {
        var currentReminderEnabled = reminderEnabled;
        var settings = new FakeSettingsService(AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                Grab = AppSettings.Default.Tasks.Grab with
                {
                    ReservationStrategy = strategy,
                    OptimalStrategyReminderEnabled = reminderEnabled
                }
            }
        });
        var coordinator = new FakeGrabSeatCoordinator();
        var notifications = new FakeNotificationService();
        var dialog = new FakeGrabStrategyReminderDialogService();
        var viewModel = new GrabPageViewModel(
            coordinator,
            new SettingsWorkflowService(settings),
            new ActivityLogService(),
            notifications,
            new FakeAppThemeService(),
            dialog,
            new FakeTimeProvider());
        var library = new LibrarySummary(1, "测试场馆", "1层", true, 100, 10, 20);
        var seats = Enumerable.Range(1, seatCount)
            .Select(index => new SeatReference($"seat-{index}", index.ToString()))
            .ToArray();

        viewModel.ApplySettings(settings.CurrentSettings);
        viewModel.ConfigureOrchestration(
            static () => true,
            static () => false,
            () => currentReminderEnabled,
            static () => Task.CompletedTask,
            enabled => currentReminderEnabled = enabled,
            () => library,
            () => seatCount,
            () => seats,
            static () => Task.CompletedTask,
            static () => { },
            static () => { },
            static () => { },
            static () => Task.CompletedTask,
            static () => Task.CompletedTask,
            static _ => { });

        return new TestContext(viewModel, coordinator, settings, notifications, dialog);
    }

    private static GrabStrategyReminderResult Result(
        GrabStrategyReminderDecision decision,
        bool disableReminder = false)
    {
        return new GrabStrategyReminderResult(decision, disableReminder);
    }

    private sealed record TestContext(
        GrabPageViewModel ViewModel,
        FakeGrabSeatCoordinator Coordinator,
        FakeSettingsService Settings,
        FakeNotificationService Notifications,
        FakeGrabStrategyReminderDialogService Dialog);
}
