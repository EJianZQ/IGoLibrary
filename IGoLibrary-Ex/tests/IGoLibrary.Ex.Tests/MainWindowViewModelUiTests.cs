using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class MainWindowViewModelUiTests
{
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
