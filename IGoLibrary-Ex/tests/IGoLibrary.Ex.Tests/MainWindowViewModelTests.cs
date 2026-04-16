using System.Net;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Avalonia.Media;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ValidateManualCookieAsync_DoesNotRestoreStoredVenueSelection_OnFreshAuthorization()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = 1,
            LastLibraryName = "场馆A"
        });
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad =
            [
                new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10),
                new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5)
            ]
        };
        var viewModel = CreateViewModel(
            sessionService: new FakeSessionService(),
            libraryService: libraryService,
            settingsService: settingsService);

        viewModel.ManualCookieText = "Authorization=a; SERVERID=b";

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthorized);
        Assert.Null(viewModel.SelectedLibrary);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);
    }

    [Fact]
    public void SidebarItems_OnlyExposeHomeAndAccount_WhenUnauthorized()
    {
        var viewModel = CreateViewModel();

        var titles = viewModel.SidebarItems.Select(item => item.Title).ToArray();

        Assert.Equal(["首页", "账户与场馆"], titles);
    }

    [Fact]
    public void SidebarItems_ExposeRestrictedEntries_WhenAuthorized()
    {
        var viewModel = CreateViewModel();

        viewModel.IsAuthorized = true;

        var titles = viewModel.SidebarItems.Select(item => item.Title).ToArray();

        Assert.Equal(["首页", "账户与场馆", "抢座", "占座", "通知设置", "系统设置"], titles);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveCookieExpiryAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.CookieAlertSmtpHost = "smtp.example.com";
        viewModel.CookieAlertSmtpPort = 465;
        viewModel.CookieEmailAlertsEnabled = true;

        await WaitForAsync(() => settingsService.SaveCalls > 0 && settingsService.CurrentSettings.CookieExpiryAlerts?.Email.SmtpHost == "smtp.example.com");

        var alerts = Assert.IsType<CookieExpiryAlertSettings>(settingsService.CurrentSettings.CookieExpiryAlerts);
        Assert.True(alerts.Email.Enabled);
        Assert.Equal("smtp.example.com", alerts.Email.SmtpHost);
        Assert.Equal(465, alerts.Email.Port);
    }

    [Fact]
    public async Task SendTestEmailAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeCookieExpiryAlertService();
        var viewModel = CreateViewModel(cookieExpiryAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.CookieAlertSmtpHost = "smtp.example.com";
        viewModel.CookieAlertSmtpPort = 587;
        viewModel.SelectedCookieAlertSecurityModeIndex = 1;
        viewModel.CookieAlertUsername = "tester";
        viewModel.CookieAlertPassword = "secret";
        viewModel.CookieAlertFromAddress = "from@example.com";
        viewModel.CookieAlertToAddress = "to@example.com";

        await viewModel.SendTestEmailAlertCommand.ExecuteAsync(null);

        var request = Assert.Single(alertService.TestEmailRequests);
        Assert.Equal("smtp.example.com", request.SmtpHost);
        Assert.Equal(587, request.Port);
        Assert.Equal(EmailSecurityMode.Tls, request.SecurityMode);
        Assert.Equal("tester", request.Username);
        Assert.Equal("secret", request.Password);
        Assert.Equal("from@example.com", request.FromAddress);
        Assert.Equal("to@example.com", request.ToAddress);
    }

    [Fact]
    public async Task SendTestEmailAlertAsync_ShowsErrorDialog_WhenSendingFails()
    {
        var alertService = new FakeCookieExpiryAlertService
        {
            SendTestEmailException = new InvalidOperationException("smtp connect failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            cookieExpiryAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.CookieAlertSmtpHost = "smtp.example.com";
        viewModel.CookieAlertFromAddress = "from@example.com";
        viewModel.CookieAlertToAddress = "to@example.com";

        await viewModel.SendTestEmailAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试邮件发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("smtp connect failed", error.ErrorMessage);
    }

    [Fact]
    public async Task TryAutoParseClipboardLinkAsync_DoesNotConsumeSameCodeTwice()
    {
        var notificationService = new FakeNotificationService();
        var apiClient = new FakeTraceIntApiClient();
        var getCookieCalls = 0;
        apiClient.OnGetCookieFromCodeAsync = (code, _) =>
        {
            getCookieCalls++;
            return Task.FromResult("Authorization=a; SERVERID=b");
        };

        var viewModel = CreateViewModel(
            apiClient: apiClient,
            notificationService: notificationService);

        const string link = "https://example.com/callback?code=1234567890abcdef1234567890abcdef&state=1";

        var firstResult = await viewModel.TryAutoParseClipboardLinkAsync(link);
        var secondResult = await viewModel.TryAutoParseClipboardLinkAsync(link);

        Assert.True(firstResult);
        Assert.False(secondResult);
        Assert.Equal(1, getCookieCalls);
        Assert.Contains(notificationService.Successes, item => item.Title == "已成功获取 Cookie");
    }

    [Fact]
    public async Task TryAutoParseClipboardLinkAsync_AllowsRetry_WhenFirstCookieFetchFailsBeforeCookieIsIssued()
    {
        var notificationService = new FakeNotificationService();
        var apiClient = new FakeTraceIntApiClient();
        var getCookieCalls = 0;
        apiClient.OnGetCookieFromCodeAsync = (_, _) =>
        {
            getCookieCalls++;
            if (getCookieCalls == 1)
            {
                throw new HttpRequestException("temporary network failure");
            }

            return Task.FromResult("Authorization=a; SERVERID=b");
        };

        var viewModel = CreateViewModel(
            apiClient: apiClient,
            notificationService: notificationService);

        const string link = "https://example.com/callback?code=1234567890abcdef1234567890abcdef&state=1";

        var firstResult = await viewModel.TryAutoParseClipboardLinkAsync(link);
        var secondResult = await viewModel.TryAutoParseClipboardLinkAsync(link);

        Assert.False(firstResult);
        Assert.True(secondResult);
        Assert.Equal(2, getCookieCalls);
        Assert.Contains(notificationService.Warnings, item => item.Title == "获取 Cookie 失败");
        Assert.Contains(notificationService.Successes, item => item.Title == "已成功获取 Cookie");
    }

    [Fact]
    public async Task SignOutAsync_ClearsStoredLastLibrarySelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = 1,
            LastLibraryName = "场馆A"
        });
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: settingsService);

        viewModel.IsAuthorized = true;
        viewModel.SelectedTabIndex = 4;
        viewModel.SelectedLibrary = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(1, sessionService.SignOutCalls);
        Assert.False(viewModel.IsAuthorized);
        Assert.Equal(MainWindowViewModel.AccountAndVenueTabIndex, viewModel.SelectedTabIndex);
        Assert.Null(viewModel.SelectedLibrary);
        Assert.Null(settingsService.CurrentSettings.LastLibraryId);
        Assert.Null(settingsService.CurrentSettings.LastLibraryName);
    }

    [Fact]
    public async Task OpenVenuePickerAsync_PreservesCurrentLockedLibrary_WhenOneIsAlreadyBound()
    {
        var libraryA = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);
        var libraryB = new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            LastLibraryId = libraryB.LibraryId,
            LastLibraryName = libraryB.Name
        });
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [libraryA, libraryB]
        };
        libraryService.LayoutsByLibraryId[libraryA.LibraryId] = new LibraryLayout(
            libraryA.LibraryId,
            libraryA.Name,
            libraryA.Floor,
            libraryA.IsOpen,
            120,
            10,
            20,
            [new SeatSnapshot("seat-1", "1", false, 0, 0)]);

        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => Task.FromResult(new LibraryRule(
                libraryA.LibraryId,
                "1小时",
                "30",
                "30",
                "0",
                "{}",
                null,
                null,
                0,
                "07:30",
                0,
                "22:00",
                -1))
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            settingsService: settingsService,
            apiClient: apiClient);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = libraryA;

        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);
        await viewModel.OpenVenuePickerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVenuePickerOpen);
        Assert.Equal(libraryA.LibraryId, viewModel.SelectedLibrary?.LibraryId);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);
    }

    [Fact]
    public async Task GrabDashboardStatusBrush_UsesFailureColor_WhenTaskCompletedByStopping()
    {
        var grabCoordinator = new FakeGrabSeatCoordinator();
        await grabCoordinator.StopAsync();

        var viewModel = CreateViewModel(grabSeatCoordinator: grabCoordinator);
        await viewModel.InitializeAsync();

        var brush = Assert.IsType<SolidColorBrush>(viewModel.GrabDashboardStatusBrush);

        Assert.Equal("已停止", viewModel.GrabDashboardStatusText);
        Assert.Equal(Color.Parse("#C93C37"), brush.Color);
    }

    [Fact]
    public async Task InitializeAsync_LoadsDashboardMetricsIntoHomeCards()
    {
        var viewModel = CreateViewModel(settingsService: new FakeSettingsService(AppSettings.Default with
        {
            SuccessfulReservationCount = 7,
            TotalGuardSeconds = 5400
        }));

        await viewModel.InitializeAsync();

        Assert.Equal(7, viewModel.HomeHistoricalSuccessCount);
        Assert.Equal("1 小时 30 分", viewModel.HomeTotalGuardDurationText);
    }

    [Fact]
    public async Task SaveSettingsAsync_PreservesDashboardMetrics()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            SuccessfulReservationCount = 4,
            TotalGuardSeconds = 7200
        });
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(4, settingsService.CurrentSettings.SuccessfulReservationCount);
        Assert.Equal(7200, settingsService.CurrentSettings.TotalGuardSeconds);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsThemePreferences()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var themeService = new FakeAppThemeService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            appThemeService: themeService);
        await viewModel.InitializeAsync();

        viewModel.SelectedAppThemeModeIndex = 2;
        viewModel.UseSystemAccent = false;

        await WaitForAsync(() => themeService.ApplySettingsCalls == 2);

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AppThemeMode.Dark, settingsService.CurrentSettings.ThemeMode);
        Assert.False(settingsService.CurrentSettings.UseSystemAccent);
        Assert.Equal(3, themeService.ApplySettingsCalls);
        Assert.Equal(AppThemeMode.Dark, themeService.LastAppliedSettings?.ThemeMode);
        Assert.False(themeService.LastAppliedSettings?.UseSystemAccent);
    }

    [Fact]
    public async Task ThemePreview_UpdatesImmediately_WithoutSavingSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var themeService = new FakeAppThemeService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            appThemeService: themeService);
        await viewModel.InitializeAsync();

        viewModel.SelectedAppThemeModeIndex = 2;
        viewModel.UseSystemAccent = false;

        await WaitForAsync(() =>
            themeService.ApplySettingsCalls == 2 &&
            themeService.LastAppliedSettings?.ThemeMode == AppThemeMode.Dark &&
            themeService.LastAppliedSettings?.UseSystemAccent == false);

        Assert.Equal(0, settingsService.SaveCalls);
        Assert.Equal(AppThemeMode.FollowSystem, settingsService.CurrentSettings.ThemeMode);
        Assert.True(settingsService.CurrentSettings.UseSystemAccent);
    }

    [Fact]
    public async Task CancelCurrentReservationAsync_ClearsHomeReservationCard_WhenApiSucceeds()
    {
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(new ReservationInfo(
                "token-1",
                1,
                "自科阅览区一",
                "seat-4",
                "4",
                DateTimeOffset.Now.AddMinutes(30))),
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true)
        };
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient,
            notificationService: notifications);

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);
        await viewModel.CancelCurrentReservationCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasNoCurrentReservation);
        Assert.Equal("--", viewModel.HomeReservationSeatNumberText);
        Assert.Contains(notifications.Successes, x => x.Title == "已取消预约");
    }

    private static MainWindowViewModel CreateViewModel(
        FakeSessionService? sessionService = null,
        FakeLibraryService? libraryService = null,
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeGrabSeatCoordinator? grabSeatCoordinator = null,
        FakeNotificationService? notificationService = null,
        FakeCookieExpiryAlertService? cookieExpiryAlertService = null,
        FakeErrorDialogService? errorDialogService = null,
        FakeAppThemeService? appThemeService = null)
    {
        return new MainWindowViewModel(
            sessionService ?? new FakeSessionService(),
            libraryService ?? new FakeLibraryService(),
            apiClient ?? new FakeTraceIntApiClient(),
            settingsService ?? new FakeSettingsService(AppSettings.Default),
            new FakeProtocolTemplateStore(new ProtocolTemplateSet("", "", "", "", "", "", "")),
            grabSeatCoordinator ?? new FakeGrabSeatCoordinator(),
            new FakeOccupySeatCoordinator(),
            cookieExpiryAlertService ?? new FakeCookieExpiryAlertService(),
            new ActivityLogService(),
            notificationService ?? new FakeNotificationService(),
            errorDialogService ?? new FakeErrorDialogService(),
            appThemeService ?? new FakeAppThemeService(),
            new AppWindowService());
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
