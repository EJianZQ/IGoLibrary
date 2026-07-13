using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowSizePersistenceServiceTests
{
    [Fact]
    public void TryGetSafeRestoredSize_ClampsToWindowMinimumAndWorkingArea()
    {
        var restored = MainWindowSizePersistenceService.TryGetSafeRestoredSize(
            new MainViewSizePreferences(true, 800, 1200),
            minWidth: 1000,
            minHeight: 680,
            workingArea: new Size(1280, 720),
            out var restoredSize);

        Assert.True(restored);
        Assert.Equal(new Size(1000, 720), restoredSize);
    }

    [Fact]
    public void TryGetSafeRestoredSize_RejectsDisabledOrInvalidPreferences()
    {
        Assert.False(MainWindowSizePersistenceService.TryGetSafeRestoredSize(
            new MainViewSizePreferences(false, 1188, 840),
            1000,
            680,
            new Size(1920, 1080),
            out _));
        Assert.False(MainWindowSizePersistenceService.TryGetSafeRestoredSize(
            new MainViewSizePreferences(true, -1, 840),
            1000,
            680,
            new Size(1920, 1080),
            out _));
    }

    [AvaloniaFact]
    public async Task InitializeAsync_AppliesRememberedSizeBeforeWindowIsShown()
    {
        var settingsService = CreateSettingsService(rememberSize: true, width: 1120, height: 720);
        var service = CreateService(settingsService, TimeSpan.FromSeconds(1));
        var window = new Window
        {
            Width = 1188,
            Height = 840,
            MinWidth = 1000,
            MinHeight = 680
        };

        await service.InitializeAsync(window);

        Assert.Equal(1120, window.Width);
        Assert.Equal(720, window.Height);
        window.Close();
    }

    [AvaloniaFact]
    public async Task EnablingRememberSize_CapturesCurrentNormalClientSizeImmediately()
    {
        var settingsService = CreateSettingsService(rememberSize: false);
        var service = CreateService(settingsService, TimeSpan.FromMinutes(1));
        var window = new Window
        {
            Width = 1188,
            Height = 840,
            MinWidth = 1000,
            MinHeight = 680
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await service.InitializeAsync(window);
            var expected = MainViewSizePreferences.Normalize(new MainViewSizePreferences(
                true,
                window.ClientSize.Width,
                window.ClientSize.Height));

            service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: true);
            await service.FlushAsync();

            var saved = Assert.IsType<MainViewSizePreferences>(settingsService.CurrentSettings.Ui.MainViewSize);
            Assert.Equal(expected.ClientWidth, saved.ClientWidth);
            Assert.Equal(expected.ClientHeight, saved.ClientHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task UserResizeBurst_DebouncesAndPersistsOnlyLatestNormalSize()
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMilliseconds(30));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);

        service.ProcessResize(new Size(1100, 700), WindowResizeReason.User, WindowState.Normal);
        service.ProcessResize(new Size(1200, 740), WindowResizeReason.User, WindowState.Normal);
        service.ProcessResize(new Size(1300.126, 760.124), WindowResizeReason.User, WindowState.Normal);

        await WaitForAsync(() => settingsService.SaveCalls == 1);

        var saved = Assert.IsType<MainViewSizePreferences>(settingsService.CurrentSettings.Ui.MainViewSize);
        Assert.Equal(1300.13, saved.ClientWidth);
        Assert.Equal(760.12, saved.ClientHeight);
        Assert.Equal(1, settingsService.SaveCalls);
    }

    [Theory]
    [InlineData(WindowResizeReason.Unspecified)]
    [InlineData(WindowResizeReason.Application)]
    [InlineData(WindowResizeReason.Layout)]
    [InlineData(WindowResizeReason.DpiChange)]
    public async Task NonUserResizeReasons_DoNotPersist(WindowResizeReason reason)
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMilliseconds(20));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);

        service.ProcessResize(new Size(1300, 760), reason, WindowState.Normal);
        await Task.Delay(60);

        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Theory]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.Minimized)]
    public async Task NonNormalWindowStates_DoNotPersistUserResize(WindowState windowState)
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMilliseconds(20));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);

        service.ProcessResize(new Size(1920, 1080), WindowResizeReason.User, windowState);
        await Task.Delay(60);

        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task DisablingRememberSize_CancelsPendingSave()
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMilliseconds(30));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);
        service.ProcessResize(new Size(1300, 760), WindowResizeReason.User, WindowState.Normal);

        service.SetRememberSizeEnabled(enabled: false, captureCurrentSize: false);
        await Task.Delay(80);

        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task FlushAsync_PersistsPendingSizeWithoutWaitingForDebounce()
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMinutes(1));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);
        service.ProcessResize(new Size(1280, 720), WindowResizeReason.User, WindowState.Normal);

        await service.FlushAsync();

        var saved = Assert.IsType<MainViewSizePreferences>(settingsService.CurrentSettings.Ui.MainViewSize);
        Assert.Equal(1280, saved.ClientWidth);
        Assert.Equal(720, saved.ClientHeight);
        Assert.Equal(1, settingsService.SaveCalls);
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.Minimized)]
    public async Task EnteringNonNormalState_DoesNotDiscardPendingNormalSize(WindowState windowState)
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        var service = CreateService(settingsService, TimeSpan.FromMinutes(1));
        var window = new Window
        {
            Width = 1188,
            Height = 840,
            MinWidth = 1000,
            MinHeight = 680
        };

        await service.InitializeAsync(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            service.ProcessResize(new Size(1280, 720), WindowResizeReason.User, WindowState.Normal);

            window.WindowState = windowState;
            Dispatcher.UIThread.RunJobs();
            await service.FlushAsync();

            var saved = Assert.IsType<MainViewSizePreferences>(settingsService.CurrentSettings.Ui.MainViewSize);
            Assert.Equal(1280, saved.ClientWidth);
            Assert.Equal(720, saved.ClientHeight);
            Assert.Equal(1, settingsService.SaveCalls);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task FailedBackgroundSave_LogsWarningAndFlushRetriesDirtySize()
    {
        var settingsService = CreateSettingsService(rememberSize: true);
        settingsService.UpdateExceptions.Enqueue(new InvalidOperationException("database unavailable"));
        var activityLog = new ActivityLogService();
        var service = new MainWindowSizePersistenceService(
            new SettingsWorkflowService(settingsService),
            activityLog,
            TimeSpan.FromMilliseconds(20));
        service.SetRememberSizeEnabled(enabled: true, captureCurrentSize: false);

        service.ProcessResize(new Size(1280, 720), WindowResizeReason.User, WindowState.Normal);
        await WaitForAsync(() => activityLog.Entries.Any(entry =>
            entry.Category == "Window" && entry.Message.Contains("database unavailable", StringComparison.Ordinal)));
        await service.FlushAsync();

        var saved = Assert.IsType<MainViewSizePreferences>(settingsService.CurrentSettings.Ui.MainViewSize);
        Assert.Equal(1280, saved.ClientWidth);
        Assert.Equal(720, saved.ClientHeight);
        Assert.Equal(1, settingsService.SaveCalls);
    }

    private static FakeSettingsService CreateSettingsService(
        bool rememberSize,
        double? width = null,
        double? height = null)
    {
        return new FakeSettingsService(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                MainViewSize = new MainViewSizePreferences(rememberSize, width, height)
            }
        });
    }

    private static MainWindowSizePersistenceService CreateService(
        FakeSettingsService settingsService,
        TimeSpan debounceDelay)
    {
        return new MainWindowSizePersistenceService(
            new SettingsWorkflowService(settingsService),
            new ActivityLogService(),
            debounceDelay);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached before the timeout.");
            }

            await Task.Delay(10);
        }
    }
}
