using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class MainWindowViewModelUiTests
{
    [AvaloniaFact]
    public async Task TaskSleepPreventionSetting_RendersBelowTraySettingAndBindsImmediately()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                PreventSystemSleepWhileTasksActive = false
            }
        });
        var runtimeService = new FakeTaskSleepPreventionService();
        var timeProvider = new FakeTimeProvider();
        var viewModel = MainWindowViewModelTests.CreateViewModel(
            settingsService: settingsService,
            timeProvider: timeProvider,
            taskSleepPreventionService: runtimeService);
        await viewModel.InitializeAsync();
        viewModel.SelectedTabIndex = 9;
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var trayToggle = Assert.IsType<ToggleSwitch>(window.FindControl<ToggleSwitch>("MinimizeToTrayToggle"));
            var sleepToggle = Assert.IsType<ToggleSwitch>(
                window.FindControl<ToggleSwitch>("PreventSystemSleepWhileTasksActiveToggle"));
            var title = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("PreventSystemSleepWhileTasksActiveTitle"));
            var description = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("PreventSystemSleepWhileTasksActiveDescription"));
            var trayCard = Assert.IsType<Border>(Assert.IsType<Grid>(trayToggle.Parent).Parent);
            var sleepCard = Assert.IsType<Border>(Assert.IsType<Grid>(sleepToggle.Parent).Parent);
            var cards = Assert.IsType<StackPanel>(trayCard.Parent).Children;

            Assert.Equal(cards.IndexOf(trayCard) + 1, cards.IndexOf(sleepCard));
            Assert.Equal("任务进行时阻止系统自动休眠", title.Text);
            Assert.Equal(
                "启用后，抢座、占座、明日预约或全域捡漏任务进行时阻止系统因空闲自动休眠；屏幕仍可按系统设置关闭",
                description.Text);
            Assert.False(sleepToggle.IsChecked);
            Assert.Equal(false, runtimeService.IsEnabled);

            sleepToggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.PreventSystemSleepWhileTasksActive);
            Assert.Equal(true, runtimeService.IsEnabled);

            timeProvider.Advance(TimeSpan.FromMilliseconds(300));
            await WaitForAsync(() =>
                settingsService.CurrentSettings.Ui.PreventSystemSleepWhileTasksActive);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkflowLogPanels_AutoScrollToNewestEntry()
    {
        var activityLogService = new ActivityLogService();
        var viewModel = MainWindowViewModelTests.CreateViewModel(
            activityLogService: activityLogService);
        await viewModel.InitializeAsync();
        viewModel.IsAuthorized = true;
        var window = new MainWindow { DataContext = viewModel };

        (int TabIndex, string Category, string ScrollViewerName)[] scenarios =
        [
            (2, "Grab", "GrabLogScrollViewer"),
            (3, "GlobalLeak", "GlobalLeakLogScrollViewer"),
            (4, "Tomorrow", "TomorrowLogScrollViewer")
        ];

        try
        {
            window.Show();
            foreach (var scenario in scenarios)
            {
                viewModel.SelectedTabIndex = scenario.TabIndex;
                Dispatcher.UIThread.RunJobs();

                var scrollViewer = Assert.IsType<ScrollViewer>(
                    window.FindControl<ScrollViewer>(scenario.ScrollViewerName));
                for (var index = 0; index < 80; index++)
                {
                    activityLogService.Write(
                        LogEntryKind.Info,
                        scenario.Category,
                        $"自动滚动验证日志 {index:D2}");
                }

                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();

                var maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                Assert.True(maximumOffset > 0, $"{scenario.ScrollViewerName} should have overflowing content.");
                Assert.InRange(Math.Abs(scrollViewer.Offset.Y - maximumOffset), 0, 1);
            }
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MobileControlToggleButton_RendersStartingTextAndSpinnerDuringSlowStartup()
    {
        var startBlocker = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mobileControlService = new FakeMobileControlService
        {
            StartBlocker = startBlocker.Task
        };
        var viewModel = MainWindowViewModelTests.CreateViewModel(
            settingsService: new FakeSettingsService(AppSettings.Default with
            {
                MobileControl = new MobileControlSettings(9527, "token")
            }),
            mobileControlService: mobileControlService);
        await viewModel.InitializeAsync();
        viewModel.SelectedTabIndex = 7;
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<Button>(window.FindControl<Button>("MobileControlToggleButton"));
        var spinner = Assert.IsType<PathIcon>(window.FindControl<PathIcon>("MobileControlStartingSpinner"));
        var buttonText = Assert.IsType<TextBlock>(
            window.FindControl<TextBlock>("MobileControlToggleButtonTextBlock"));
        var startTask = viewModel.ToggleMobileControlCommand.ExecuteAsync(null);

        try
        {
            await mobileControlService.StartEntered.Task;
            Dispatcher.UIThread.RunJobs();

            Assert.False(viewModel.ToggleMobileControlCommand.CanExecute(null));
            Assert.True(spinner.IsVisible);
            Assert.Equal("正在启动中", buttonText.Text);
            Assert.IsType<RotateTransform>(spinner.RenderTransform);

            startBlocker.SetResult(null);
            await startTask;
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.ToggleMobileControlCommand.CanExecute(null));
            Assert.False(spinner.IsVisible);
            Assert.Equal("停用手机控制", buttonText.Text);
        }
        finally
        {
            startBlocker.TrySetResult(null);
            await startTask;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OpenModalOverlay_BlocksSidebarPointerInputWithoutDimmingIt()
    {
        var viewModel = MainWindowViewModelTests.CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.IsAuthorized = true;
        viewModel.SelectedTabIndex = 2;
        var alertSoundService = new RecordingAlertSoundService();
        var window = new MainWindow(
            new AppWindowService(),
            new NoOpNotificationService(),
            alertSoundService)
        {
            DataContext = viewModel
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var sidebar = Assert.IsType<ListBox>(window.FindControl<ListBox>("SidebarNavigationList"));
            Assert.True(sidebar.IsEnabled);
            Assert.True(sidebar.IsHitTestVisible);

            viewModel.IsGrabSeatSelectionOverlayOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(sidebar.IsEnabled);
            Assert.False(sidebar.IsHitTestVisible);
            Assert.Equal(2, viewModel.SelectedTabIndex);

            var modal = Assert.IsType<Border>(window.FindControl<Border>("GrabSeatSelectionModal"));
            Assert.True(window.NotifyBlockedNavigationAttempt());
            Assert.IsType<TransformGroup>(modal.RenderTransform);
            Assert.Equal(1, alertSoundService.SystemPromptPlayCount);
            Assert.True(window.NotifyBlockedNavigationAttempt());
            Assert.Equal(1, alertSoundService.SystemPromptPlayCount);

            viewModel.IsGrabSeatSelectionOverlayOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(sidebar.IsEnabled);
            Assert.True(sidebar.IsHitTestVisible);
            Assert.Null(modal.RenderTransform);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GlobalLeakLibraryPicker_DisablesMutation_WhenTaskBecomesActive()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var coordinator = new FakeGlobalLeakCoordinator();
        var viewModel = MainWindowViewModelTests.CreateGlobalLeakViewModel(
            settingsService: settingsService,
            globalLeakCoordinator: coordinator);
        await viewModel.InitializeAsync();
        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;

        var timestamp = DateTimeOffset.Now;
        coordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "全域捡漏",
            "运行中",
            timestamp,
            timestamp,
            Reason: CoordinatorStatusReason.Running));
        Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.CanEditGlobalLeakConfiguration);
        Assert.True(viewModel.CanCancelGlobalLeakLibraryPicker);
        Assert.False(viewModel.SetGlobalLeakLibraryDropIndicator(1, insertAfter: true));
        Assert.False(viewModel.MoveDraftGlobalLeakLibrary(1, 2, insertAfter: true));
        await viewModel.SelectAllGlobalLeakLibrariesCommand.ExecuteAsync(null);
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);
        Assert.Equal([1], viewModel.DraftGlobalLeakLibraryPriorities.Select(x => x.LibraryId).ToArray());
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);
        Assert.Equal(0, settingsService.SaveCalls);

        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(Assert.IsType<Grid>(window.FindControl<Grid>("GlobalLeakLibraryPickerActions")).IsEnabled);
            Assert.False(Assert.IsType<Grid>(window.FindControl<Grid>("GlobalLeakLibraryPickerColumns")).IsEnabled);
            Assert.False(Assert.IsType<Button>(window.FindControl<Button>("GlobalLeakLibraryPickerConfirmButton")).IsEnabled);
            Assert.True(Assert.IsType<Button>(window.FindControl<Button>("GlobalLeakLibraryPickerCloseButton")).IsEnabled);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }

        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);
        Assert.False(viewModel.IsGlobalLeakLibraryPickerOpen);
    }

    [AvaloniaFact]
    public async Task RememberWindowSizeSetting_LoadsBindsAndAutoSavesWithCaptureSemantics()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                MainViewSize = new MainViewSizePreferences(true, 1260, 780)
            }
        });
        var sizePersistence = new RecordingMainWindowSizePersistenceService();
        var timeProvider = new FakeTimeProvider();
        var viewModel = MainWindowViewModelTests.CreateViewModel(
            settingsService: settingsService,
            timeProvider: timeProvider,
            windowSizePersistenceService: sizePersistence);

        await viewModel.InitializeAsync();
        var saveCallsAfterLoad = settingsService.SaveCalls;
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.SystemSettings.RememberWindowSizeEnabled);
            Assert.True(window.FindControl<ToggleSwitch>("RememberWindowSizeToggle")?.IsChecked);
            Assert.Contains((Enabled: true, CaptureCurrentSize: false), sizePersistence.Changes);
            Assert.Equal(saveCallsAfterLoad, settingsService.SaveCalls);

            viewModel.SystemSettings.RememberWindowSizeEnabled = false;
            timeProvider.Advance(TimeSpan.FromMilliseconds(300));
            await WaitForAsync(() => settingsService.CurrentSettings.Ui.MainViewSize?.RememberSize == false);
            Assert.Equal((Enabled: false, CaptureCurrentSize: false), sizePersistence.Changes[^1]);

            viewModel.SystemSettings.RememberWindowSizeEnabled = true;
            timeProvider.Advance(TimeSpan.FromMilliseconds(300));
            await WaitForAsync(() => settingsService.CurrentSettings.Ui.MainViewSize?.RememberSize == true);
            Assert.Equal((Enabled: true, CaptureCurrentSize: true), sizePersistence.Changes[^1]);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CloudflareTunnelInterruptionAlertToggle_TracksNetworkModeWithoutResettingValue()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(NetworkMode: MobileControlNetworkMode.LocalNetwork),
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = TaskEventAlertSettings.Default with
                {
                    Events = TaskEventAlertEventSettings.Default with
                    {
                        CloudflareTunnelInterrupted = false
                    }
                }
            }
        });
        var exposureManager = new FakeNetworkExposureManager();
        var viewModel = MainWindowViewModelTests.CreateViewModel(
            settingsService: settingsService,
            networkExposureManager: exposureManager);
        await viewModel.InitializeAsync();
        viewModel.IsAuthorized = true;
        viewModel.SelectedTabIndex = 8;

        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var card = Assert.IsType<Border>(
                window.FindControl<Border>("CloudflareTunnelInterruptedAlertCard"));
            Assert.False(card.IsVisible);
            Assert.False(viewModel.CloudflareTunnelInterruptedAlertsEnabled);

            viewModel.SystemSettings.SelectedMobileControlNetworkModeIndex =
                (int)MobileControlNetworkMode.CloudflareTunnel;
            await WaitForAsync(() => viewModel.SystemSettings.IsCloudflareTunnelSelected);
            Dispatcher.UIThread.RunJobs();

            Assert.True(card.IsVisible);
            Assert.False(viewModel.CloudflareTunnelInterruptedAlertsEnabled);

            exposureManager.SimulateModeChange(
                MobileControlNetworkMode.LocalNetwork,
                "Cloudflare Tunnel 不可用，已自动回退到本机局域网");
            await WaitForAsync(() => !viewModel.SystemSettings.IsCloudflareTunnelSelected);
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.IsVisible);

            viewModel.SystemSettings.SelectedMobileControlNetworkModeIndex =
                (int)MobileControlNetworkMode.CloudflareTunnel;
            await WaitForAsync(() => viewModel.SystemSettings.IsCloudflareTunnelSelected);
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsVisible);
            Assert.False(viewModel.CloudflareTunnelInterruptedAlertsEnabled);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }
}
