using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlPageViewModelTests
{
    [Fact]
    public async Task StartMobileControlAsync_GeneratesSettingsStartsServiceAndCreatesQrCode()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default, normalizeMobileControl: false);
        var mobileControlService = new FakeMobileControlService();
        var qrCodeImageFactory = new FakeQrCodeImageFactory();
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(settingsService),
            qrCodeImageFactory,
            new ActivityLogService(),
            new FakeNotificationService());

        await viewModel.StartMobileControlCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMobileControlRunning);
        Assert.Equal(1, mobileControlService.StartCalls);
        Assert.InRange(
            viewModel.MobileControlPort,
            MobileControlSettings.RandomPortMinInclusive,
            MobileControlSettings.RandomPortMaxExclusive - 1);
        Assert.Equal(settingsService.CurrentSettings.MobileControl.Port, viewModel.MobileControlPort);
        Assert.Contains($"http://127.0.0.1:{viewModel.MobileControlPort}/?token=", viewModel.MobileControlUrlText);
        Assert.Single(qrCodeImageFactory.CreatedTexts);
        Assert.Equal(viewModel.MobileControlUrlText, qrCodeImageFactory.CreatedTexts[0]);
    }

    [Fact]
    public async Task StartMobileControlAsync_DoesNotWriteAccessTokenToActivityLog()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "secret-token")
        });
        var logs = new ActivityLogService();
        var viewModel = new MobileControlPageViewModel(
            new FakeMobileControlService(),
            new SettingsWorkflowService(settingsService),
            new FakeQrCodeImageFactory(),
            logs,
            new FakeNotificationService());

        await viewModel.StartMobileControlCommand.ExecuteAsync(null);

        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Message.Contains("secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RandomizeMobileControlPortAsync_WhenRunning_RestartsServiceWithNewPort()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token")
        });
        var mobileControlService = new FakeMobileControlService();
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(settingsService),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());

        await viewModel.StartMobileControlCommand.ExecuteAsync(null);
        await viewModel.RandomizeMobileControlPortCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMobileControlRunning);
        Assert.Equal(2, mobileControlService.StartCalls);
        Assert.Equal(1, mobileControlService.StopCalls);
        Assert.NotEqual(9527, viewModel.MobileControlPort);
        Assert.Equal("token", settingsService.CurrentSettings.MobileControl.AccessToken);
    }

    [Fact]
    public async Task ToggleMobileControlAsync_StartsAndStopsService()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token")
        });
        var mobileControlService = new FakeMobileControlService();
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(settingsService),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());

        Assert.Equal("启用手机控制", viewModel.MobileControlToggleButtonText);

        await viewModel.ToggleMobileControlCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsMobileControlRunning);
        Assert.Equal("停用手机控制", viewModel.MobileControlToggleButtonText);
        Assert.Equal(1, mobileControlService.StartCalls);

        await viewModel.ToggleMobileControlCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsMobileControlRunning);
        Assert.Equal("启用手机控制", viewModel.MobileControlToggleButtonText);
        Assert.Equal(1, mobileControlService.StopCalls);
        Assert.True(viewModel.HasNoMobileControlQrCode);
    }

    [Fact]
    public void MobileControlDetailsCommands_OpenCloseAndExposeFullAccessToken()
    {
        var viewModel = new MobileControlPageViewModel(
            new FakeMobileControlService(),
            new SettingsWorkflowService(new FakeSettingsService(AppSettings.Default)),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());

        viewModel.ApplySettings(new MobileControlSettings(9527, "full-access-token"));
        viewModel.OpenMobileControlDetailsCommand.Execute(null);

        Assert.True(viewModel.IsMobileControlDetailsOpen);
        Assert.Equal("full-access-token", viewModel.MobileControlAccessTokenFullText);

        viewModel.CloseMobileControlDetailsCommand.Execute(null);

        Assert.False(viewModel.IsMobileControlDetailsOpen);
    }

    [Fact]
    public async Task AutoStartToggle_PersistsSettingAndStartsImmediately()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token")
        });
        var mobileControlService = new FakeMobileControlService();
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(settingsService),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());
        viewModel.ApplySettings(settingsService.CurrentSettings.MobileControl);

        viewModel.IsMobileControlAutoStartEnabled = true;

        await WaitForAsync(() =>
            settingsService.CurrentSettings.MobileControl.AutoStart &&
            mobileControlService.StartCalls == 1);
        Assert.True(viewModel.IsMobileControlRunning);
    }

    [Fact]
    public async Task StartAutomaticallyIfEnabledAsync_StartsWhenEnabledWithoutAuthorization()
    {
        var mobileControlService = new FakeMobileControlService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token", true)
        });
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(settingsService),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());
        viewModel.ApplySettings(settingsService.CurrentSettings.MobileControl);

        await viewModel.StartAutomaticallyIfEnabledAsync();

        Assert.Equal(1, mobileControlService.StartCalls);
        Assert.True(viewModel.IsMobileControlRunning);
    }

    [Fact]
    public async Task StartAutomaticallyIfEnabledAsync_DoesNotStartWhenDisabled()
    {
        var mobileControlService = new FakeMobileControlService();
        var viewModel = new MobileControlPageViewModel(
            mobileControlService,
            new SettingsWorkflowService(new FakeSettingsService(AppSettings.Default)),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            new FakeNotificationService());
        viewModel.ApplySettings(new MobileControlSettings(9527, "token"));

        await viewModel.StartAutomaticallyIfEnabledAsync();

        Assert.Equal(0, mobileControlService.StartCalls);
        Assert.False(viewModel.IsMobileControlRunning);
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

            await Task.Delay(25);
        }

        throw new TimeoutException("Condition was not met within the expected time.");
    }
}
