using System.Net;
using System.Text;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Avalonia.Media;
using Avalonia.Threading;

namespace IGoLibrary.Ex.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task ValidateManualCookieAsync_DoesNotRestoreStoredVenueSelection_OnFreshAuthorization()
    {
        var settingsService = new FakeSettingsService(WithVenue(1, "场馆A"));
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

        Assert.Equal(["首页", "账户与场馆", "抢座", "明日预约", "占座", "通知设置", "系统设置"], titles);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveTaskEventAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.EmailAlertSmtpHost = "smtp.example.com";
        viewModel.EmailAlertSmtpPort = 465;
        viewModel.EmailAlertsEnabled = true;

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Email.SmtpHost == "smtp.example.com");

        var alerts = Assert.IsType<TaskEventAlertSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts);
        Assert.True(alerts.Email.Enabled);
        Assert.Equal("smtp.example.com", alerts.Email.SmtpHost);
        Assert.Equal(465, alerts.Email.Port);
    }

    [Fact]
    public async Task SendTestEmailAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeTaskEventAlertDispatcher();
        var viewModel = CreateViewModel(taskAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.EmailAlertSmtpHost = "smtp.example.com";
        viewModel.EmailAlertSmtpPort = 587;
        viewModel.SelectedEmailAlertSecurityModeIndex = 1;
        viewModel.EmailAlertUsername = "tester";
        viewModel.EmailAlertPassword = "secret";
        viewModel.EmailAlertFromAddress = "from@example.com";
        viewModel.EmailAlertToAddress = "to@example.com";

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
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestEmailException = new InvalidOperationException("smtp connect failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            taskAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.EmailAlertSmtpHost = "smtp.example.com";
        viewModel.EmailAlertFromAddress = "from@example.com";
        viewModel.EmailAlertToAddress = "to@example.com";

        await viewModel.SendTestEmailAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试邮件发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("smtp connect failed", error.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_LoadsTelegramNotificationSettings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                new TelegramAlertChannelSettings(true, "https://telegram.example.com", "token-1", "chat-1"))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.TelegramAlertsEnabled);
        Assert.Equal("https://telegram.example.com", viewModel.TelegramAlertApiBaseUrl);
        Assert.Equal("token-1", viewModel.TelegramAlertBotToken);
        Assert.Equal("chat-1", viewModel.TelegramAlertChatId);
    }

    [Fact]
    public async Task InitializeAsync_DefaultsNullTelegramNotificationStrings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                new TelegramAlertChannelSettings(true, null!, null!, null!))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.TelegramAlertsEnabled);
        Assert.Equal(TelegramAlertChannelSettings.DefaultApiBaseUrl, viewModel.TelegramAlertApiBaseUrl);
        Assert.Equal(string.Empty, viewModel.TelegramAlertBotToken);
        Assert.Equal(string.Empty, viewModel.TelegramAlertChatId);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveTelegramAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.TelegramAlertsEnabled = true;
        viewModel.TelegramAlertApiBaseUrl = "https://telegram.example.com/";
        viewModel.TelegramAlertBotToken = " token-1 ";
        viewModel.TelegramAlertChatId = " chat-1 ";

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Telegram.BotToken == "token-1");

        var telegram = Assert.IsType<TelegramAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Telegram);
        Assert.True(telegram.Enabled);
        Assert.Equal("https://telegram.example.com", telegram.ApiBaseUrl);
        Assert.Equal("token-1", telegram.BotToken);
        Assert.Equal("chat-1", telegram.ChatId);
    }

    [Fact]
    public async Task SendTestTelegramAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeTaskEventAlertDispatcher();
        var viewModel = CreateViewModel(taskAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.TelegramAlertsEnabled = true;
        viewModel.TelegramAlertApiBaseUrl = "https://telegram.example.com/";
        viewModel.TelegramAlertBotToken = " token-1 ";
        viewModel.TelegramAlertChatId = " chat-1 ";

        await viewModel.SendTestTelegramAlertCommand.ExecuteAsync(null);

        var request = Assert.Single(alertService.TestTelegramRequests);
        Assert.True(request.Enabled);
        Assert.Equal("https://telegram.example.com", request.ApiBaseUrl);
        Assert.Equal("token-1", request.BotToken);
        Assert.Equal("chat-1", request.ChatId);
    }

    [Fact]
    public async Task SendTestTelegramAlertAsync_ShowsErrorDialog_WhenSendingFails()
    {
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestTelegramException = new InvalidOperationException("telegram send failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            taskAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.TelegramAlertApiBaseUrl = "https://telegram.example.com";
        viewModel.TelegramAlertBotToken = "token-1";
        viewModel.TelegramAlertChatId = "chat-1";

        await viewModel.SendTestTelegramAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试 Telegram 发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("telegram send failed", error.ErrorMessage);
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
    public async Task TryAutoParseClipboardLinkAsync_ShowsCookieExpirationTime_WhenJwtCookieHasExpireAt()
    {
        var notificationService = new FakeNotificationService();
        var expiresAt = new DateTimeOffset(2026, 5, 5, 16, 56, 0, DateTimeOffset.Now.Offset);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult(BuildAuthorizationCookie(expiresAt))
        };
        var viewModel = CreateViewModel(
            apiClient: apiClient,
            notificationService: notificationService);

        const string link = "https://example.com/callback?code=1234567890abcdef1234567890abcdef&state=1";

        var result = await viewModel.TryAutoParseClipboardLinkAsync(link);

        Assert.True(result);
        var success = Assert.Single(notificationService.Successes, item => item.Title == "已成功获取 Cookie");
        Assert.Equal(
            $"授权链接解析成功，Cookie 已填入。{Environment.NewLine}Cookie 到期时间：5月5日 16:56",
            success.Message);
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
    public async Task InitializeAsync_ShowsSuccessToast_WhenStoredJwtCookieIsRestored()
    {
        var notificationService = new FakeNotificationService();
        var expiresAt = DateTimeOffset.Now.AddHours(2);
        var sessionService = new FakeSessionService
        {
            RestoreResult = new SessionCredentials(
                BuildAuthorizationCookie(expiresAt),
                SessionSource.ManualCookie,
                DateTimeOffset.Now,
                true)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            notificationService: notificationService);

        await viewModel.InitializeAsync();

        var success = Assert.Single(notificationService.Successes, item => item.Title == "已成功恢复上次的 Cookie");
        Assert.Equal($"Cookie 到期时间：{expiresAt:M月d日 HH:mm}", success.Message);
    }

    [Fact]
    public async Task InitializeAsync_ShowsWarningToast_WhenRestoredJwtCookieExpiresSoon()
    {
        var notificationService = new FakeNotificationService();
        var expiresAt = DateTimeOffset.Now.AddMinutes(20);
        var sessionService = new FakeSessionService
        {
            RestoreResult = new SessionCredentials(
                BuildAuthorizationCookie(expiresAt),
                SessionSource.ManualCookie,
                DateTimeOffset.Now,
                true)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            notificationService: notificationService);

        await viewModel.InitializeAsync();

        var warning = Assert.Single(notificationService.Warnings, item => item.Title == "已成功恢复上次的 Cookie，注意到期时间");
        Assert.Equal($"Cookie 到期时间：{expiresAt:M月d日 HH:mm}", warning.Message);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_ShowsSidebarCookieExpiration_WhenJwtCookieHasExpireAt()
    {
        var expiresAt = new DateTimeOffset(2026, 5, 5, 16, 56, 0, DateTimeOffset.Now.Offset);
        var viewModel = CreateViewModel();
        viewModel.ManualCookieText = BuildAuthorizationCookie(expiresAt);

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasSidebarSessionExpiration);
        Assert.Equal("5月5日 16:56", viewModel.SidebarSessionExpirationText);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_UsesWarningBrush_WhenCookieExpiresWithinThirtyMinutes()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualCookieText = BuildAuthorizationCookie(DateTimeOffset.Now.AddMinutes(20));

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Equal("#FFC27803", GetBrushColor(viewModel.SidebarSessionExpirationBrush).ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_UsesFailureBrush_WhenCookieExpiresWithinTenMinutes()
    {
        var viewModel = CreateViewModel();
        viewModel.ManualCookieText = BuildAuthorizationCookie(DateTimeOffset.Now.AddMinutes(5));

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Equal("#FFC93C37", GetBrushColor(viewModel.SidebarSessionExpirationBrush).ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task SignOutAsync_HidesSidebarCookieExpiration()
    {
        var expiresAt = DateTimeOffset.Now.AddHours(2);
        var viewModel = CreateViewModel();
        viewModel.ManualCookieText = BuildAuthorizationCookie(expiresAt);
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasSidebarSessionExpiration);
        Assert.Equal(string.Empty, viewModel.SidebarSessionExpirationText);
    }

    [Fact]
    public async Task SignOutAsync_ClearsStoredLastLibrarySelection()
    {
        var settingsService = new FakeSettingsService(WithVenue(1, "场馆A"));
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
        Assert.Null(settingsService.CurrentSettings.Venue.LastLibraryId);
        Assert.Null(settingsService.CurrentSettings.Venue.LastLibraryName);
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
        var settingsService = new FakeSettingsService(WithVenue(libraryB.LibraryId, libraryB.Name));
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
    public async Task RefreshSeatsAsync_PreservesVenueRulePresentation()
    {
        var library = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [library]
        };
        libraryService.LayoutsByLibraryId[library.LibraryId] = new LibraryLayout(
            library.LibraryId,
            library.Name,
            library.Floor,
            library.IsOpen,
            120,
            10,
            20,
            [new SeatSnapshot("seat-1", "1", false, 0, 0)]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => Task.FromResult(new LibraryRule(
                library.LibraryId,
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
            apiClient: apiClient);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = library;

        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);
        await viewModel.RefreshSeatsCommand.ExecuteAsync(null);

        Assert.Equal("07:30", viewModel.VenueOpenTimeText);
        Assert.Equal("22:00", viewModel.VenueCloseTimeText);
    }

    [Fact]
    public async Task BindSelectedLibraryAsync_LogsRuleFailure_WithoutFailingBinding()
    {
        var library = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [library]
        };
        libraryService.LayoutsByLibraryId[library.LibraryId] = new LibraryLayout(
            library.LibraryId,
            library.Name,
            library.Floor,
            library.IsOpen,
            120,
            10,
            20,
            [new SeatSnapshot("seat-1", "1", false, 0, 0)]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryRuleAsync = (_, _, _) => throw new InvalidOperationException("rule failed")
        };
        var activityLogService = new ActivityLogService();
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            apiClient: apiClient,
            activityLogService: activityLogService);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = library;

        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);

        Assert.Equal("场馆A / 3层 / 余座 90", viewModel.LibrarySummary);
        Assert.Equal("--", viewModel.VenueOpenTimeText);
        Assert.Equal("--", viewModel.VenueCloseTimeText);
        Assert.Contains(activityLogService.Entries, entry =>
            entry.Kind == LogEntryKind.Warning &&
            entry.Category == "Library" &&
            entry.Message.Contains("加载场馆开放时间失败：rule failed", StringComparison.Ordinal));
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
    public async Task GrabSuccessMetrics_UseStatusReason_NotSuccessMessageText()
    {
        var settingsService = new FakeSettingsService(WithDashboard(0, 0));
        var grabCoordinator = new FakeGrabSeatCoordinator();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            grabSeatCoordinator: grabCoordinator);
        await viewModel.InitializeAsync();

        grabCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "抢座",
            "预约流程完成",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.GrabSucceeded));
        Dispatcher.UIThread.RunJobs();
        await WaitForAsync(() => settingsService.SaveCalls > 0);

        Assert.Equal(1, viewModel.HomeHistoricalSuccessCount);
        Assert.Equal(1, settingsService.CurrentSettings.Dashboard.SuccessfulReservationCount);
    }

    [Fact]
    public async Task InitializeAsync_LoadsDashboardMetricsIntoHomeCards()
    {
        var viewModel = CreateViewModel(settingsService: new FakeSettingsService(WithDashboard(7, 5400)));

        await viewModel.InitializeAsync();

        Assert.Equal(7, viewModel.HomeHistoricalSuccessCount);
        Assert.Equal("1 小时 30 分", viewModel.HomeTotalGuardDurationText);
    }

    [Fact]
    public async Task SaveSettingsAsync_PreservesDashboardMetrics()
    {
        var settingsService = new FakeSettingsService(WithDashboard(4, 7200));
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();
        viewModel.HomeHistoricalSuccessCount = 99;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(4, settingsService.CurrentSettings.Dashboard.SuccessfulReservationCount);
        Assert.Equal(7200, settingsService.CurrentSettings.Dashboard.TotalGuardSeconds);
    }

    [Fact]
    public async Task SaveSettingsAsync_PreservesStoredVenueSelection()
    {
        var settingsService = new FakeSettingsService(WithVenue(12, "自科阅览区一"));
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(12, settingsService.CurrentSettings.Venue.LastLibraryId);
        Assert.Equal("自科阅览区一", settingsService.CurrentSettings.Venue.LastLibraryName);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsTelegramNotificationSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.TelegramAlertsEnabled = true;
        viewModel.TelegramAlertApiBaseUrl = "https://telegram.example.com/";
        viewModel.TelegramAlertBotToken = " token-1 ";
        viewModel.TelegramAlertChatId = " chat-1 ";

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var telegram = Assert.IsType<TelegramAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Telegram);
        Assert.True(telegram.Enabled);
        Assert.Equal("https://telegram.example.com", telegram.ApiBaseUrl);
        Assert.Equal("token-1", telegram.BotToken);
        Assert.Equal("chat-1", telegram.ChatId);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsThemePreferences()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var themeService = new FakeAppThemeService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            appThemeService: themeService);
        await viewModel.InitializeAsync();

        viewModel.SelectedAppThemeModeIndex = 2;
        viewModel.UseSystemAccent = false;

        await WaitForAsync(() => themeService.ApplySettingsCalls == 2);

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal(AppThemeMode.Dark, settingsService.CurrentSettings.Ui.Theme?.Mode);
        Assert.False(settingsService.CurrentSettings.Ui.Theme?.UseSystemAccent);
        Assert.Equal(3, themeService.ApplySettingsCalls);
        Assert.Equal(AppThemeMode.Dark, themeService.LastAppliedTheme?.Mode);
        Assert.False(themeService.LastAppliedTheme?.UseSystemAccent);
    }

    [Fact]
    public async Task ThemePreview_UpdatesImmediately_WithoutSavingSettings()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var themeService = new FakeAppThemeService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            appThemeService: themeService);
        await viewModel.InitializeAsync();

        viewModel.SelectedAppThemeModeIndex = 2;
        viewModel.UseSystemAccent = false;

        await WaitForAsync(() =>
            themeService.ApplySettingsCalls == 2 &&
            themeService.LastAppliedTheme?.Mode == AppThemeMode.Dark &&
            themeService.LastAppliedTheme?.UseSystemAccent == false);

        Assert.Equal(0, settingsService.SaveCalls);
        Assert.Equal(AppThemeMode.FollowSystem, settingsService.CurrentSettings.Ui.Theme?.Mode);
        Assert.True(settingsService.CurrentSettings.Ui.Theme?.UseSystemAccent);
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

    [Fact]
    public async Task TomorrowSeatSelection_IsSingleChoice_AndDoesNotMutateGrabSelection()
    {
        var (viewModel, _) = await CreateBoundTomorrowViewModelAsync();

        viewModel.VisibleSeats[0].IsSelected = true;
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[1].IsSelected = true;

        Assert.True(viewModel.IsTomorrowSeatSelectionOverlayOpen);
        Assert.Equal(3, viewModel.TomorrowVisibleSeats.Count);
        Assert.Contains(viewModel.TomorrowVisibleSeats, seat => seat.SeatKey == "seat-2" && seat.IsOccupied);
        Assert.Single(viewModel.SelectedSeats);
        Assert.Equal("seat-1", viewModel.SelectedSeats[0].SeatKey);
        Assert.True(viewModel.VisibleSeats[0].IsSelected);
        Assert.Null(viewModel.SelectedTomorrowSeat);
        Assert.Equal("本次已选择 2", viewModel.DraftSelectedTomorrowSeatSummaryText);

        viewModel.TomorrowVisibleSeats[2].IsSelected = true;

        Assert.Null(viewModel.SelectedTomorrowSeat);
        Assert.Equal("本次已选择 3", viewModel.DraftSelectedTomorrowSeatSummaryText);
        Assert.False(viewModel.TomorrowVisibleSeats[1].IsSelected);
        Assert.True(viewModel.TomorrowVisibleSeats[2].IsSelected);
        Assert.Single(viewModel.SelectedSeats);
        Assert.Equal("seat-1", viewModel.SelectedSeats[0].SeatKey);

        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);

        Assert.False(viewModel.IsTomorrowSeatSelectionOverlayOpen);
        Assert.Equal("seat-3", viewModel.SelectedTomorrowSeat?.SeatKey);
    }

    [Fact]
    public async Task CancelTomorrowSeatSelection_RestoresPreviouslySelectedSeat()
    {
        var (viewModel, _) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);

        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[2].IsSelected = true;
        viewModel.CancelTomorrowSeatSelectionCommand.Execute(null);

        Assert.False(viewModel.IsTomorrowSeatSelectionOverlayOpen);
        Assert.Equal("seat-1", viewModel.SelectedTomorrowSeat?.SeatKey);
        Assert.True(viewModel.TomorrowVisibleSeats[0].IsSelected);
        Assert.False(viewModel.TomorrowVisibleSeats[2].IsSelected);
    }

    [Fact]
    public async Task RunTomorrowReservationNowAsync_BuildsImmediateSingleSeatPlan()
    {
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[1].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.TomorrowScheduledStartTime = new TimeSpan(20, 0, 0);

        await viewModel.RunTomorrowReservationNowCommand.ExecuteAsync(null);

        var plan = Assert.IsType<TomorrowReservationPlan>(coordinator.LastPlan);
        Assert.Equal(117580, plan.LibraryId);
        Assert.Equal("自科阅览区一", plan.LibraryName);
        Assert.Equal(new SeatReference("seat-2", "2"), plan.Seat);
        Assert.Equal(new TimeOnly(20, 0, 0), plan.ScheduledStart);
        Assert.True(plan.ExecuteImmediately);
    }

    [Fact]
    public async Task StartTomorrowReservationAsync_BuildsScheduledSingleSeatPlan()
    {
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.TomorrowScheduledStartTime = new TimeSpan(22, 1, 2);

        await viewModel.StartTomorrowReservationCommand.ExecuteAsync(null);

        var plan = Assert.IsType<TomorrowReservationPlan>(coordinator.LastPlan);
        Assert.Equal(new SeatReference("seat-1", "1"), plan.Seat);
        Assert.Equal(new TimeOnly(22, 1, 2), plan.ScheduledStart);
        Assert.False(plan.ExecuteImmediately);
    }

    [Fact]
    public async Task InitializeAsync_LoadsScheduledStartDefaults()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings() with
        {
            Tasks = new TaskExecutionSettings(
                new GrabTaskSettings(GrabReservationStrategy.QueryThenReserve, new TimeSpan(7, 59, 55)),
                OccupyTaskSettings.Default,
                new TomorrowReservationTaskSettings(new TimeSpan(22, 1, 2)))
        });
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.Equal((TimeSpan?)new TimeSpan(7, 59, 55), viewModel.ScheduledStartTime);
        Assert.Equal((TimeSpan?)new TimeSpan(22, 1, 2), viewModel.TomorrowScheduledStartTime);
        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task ScheduledStartDefaults_AutoSaveSilently_WhenTimePickerValuesChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            notificationService: notifications);
        await viewModel.InitializeAsync();

        viewModel.ScheduledStartTime = new TimeSpan(7, 59, 55);
        viewModel.TomorrowScheduledStartTime = new TimeSpan(22, 1, 2);

        await WaitForAsync(() =>
            settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime == new TimeSpan(7, 59, 55) &&
            settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime == new TimeSpan(22, 1, 2));

        Assert.Empty(notifications.Infos);
        Assert.Empty(notifications.Warnings);
        Assert.Empty(notifications.Successes);
    }

    [Fact]
    public async Task ScheduledStartDefaults_FlushPendingSavesImmediately_WhenAutoSaveDelayHasNotElapsed()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            notificationService: notifications);
        await viewModel.InitializeAsync();

        viewModel.ScheduledStartTime = new TimeSpan(7, 59, 55);
        viewModel.TomorrowScheduledStartTime = new TimeSpan(22, 1, 2);

        await viewModel.FlushPendingScheduledStartDefaultsAsync();
        var saveCallsAfterFlush = settingsService.SaveCalls;
        await Task.Delay(650);

        Assert.Equal(new TimeSpan(7, 59, 55), settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(new TimeSpan(22, 1, 2), settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
        Assert.Equal(saveCallsAfterFlush, settingsService.SaveCalls);
        Assert.Empty(notifications.Infos);
        Assert.Empty(notifications.Warnings);
        Assert.Empty(notifications.Successes);
    }

    [Fact]
    public async Task ScheduledStartDefaults_DoNotAutoSave_WhenTimePickerValuesAreOutOfRange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.ScheduledStartTime = TimeSpan.FromDays(1);
        viewModel.TomorrowScheduledStartTime = TimeSpan.FromSeconds(-1);
        await Task.Delay(650);

        Assert.Equal(0, settingsService.SaveCalls);
        Assert.Equal(TimeSpan.Zero, settingsService.CurrentSettings.Tasks.Grab.DefaultScheduledStartTime);
        Assert.Equal(new TimeSpan(20, 0, 0), settingsService.CurrentSettings.Tasks.TomorrowReservation.DefaultScheduledStartTime);
    }

    [Fact]
    public async Task TomorrowScheduledStartTime_RestoresCurrentDefault_WhenTimePickerCleared()
    {
        var (viewModel, _) = await CreateBoundTomorrowViewModelAsync();
        viewModel.TomorrowScheduledStartTime = new TimeSpan(22, 1, 2);

        viewModel.TomorrowScheduledStartTime = null;

        Assert.Equal((TimeSpan?)new TimeSpan(22, 1, 2), viewModel.TomorrowScheduledStartTime);
    }

    [Fact]
    public async Task StartTomorrowReservationAsync_UsesCurrentDefault_WhenTimePickerCleared()
    {
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.TomorrowScheduledStartTime = new TimeSpan(22, 1, 2);
        viewModel.TomorrowScheduledStartTime = null;

        await viewModel.StartTomorrowReservationCommand.ExecuteAsync(null);

        var plan = Assert.IsType<TomorrowReservationPlan>(coordinator.LastPlan);
        Assert.Equal(new TimeOnly(22, 1, 2), plan.ScheduledStart);
        Assert.False(plan.ExecuteImmediately);
    }

    [Fact]
    public async Task StartTomorrowReservationAsync_ShowsWarning_WhenScheduledStartTimeOutOfRange()
    {
        var notifications = new FakeNotificationService();
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync(notificationService: notifications);
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.TomorrowScheduledStartTime = TimeSpan.FromDays(1);

        await viewModel.StartTomorrowReservationCommand.ExecuteAsync(null);

        Assert.Null(coordinator.LastPlan);
        Assert.Contains(
            notifications.Warnings,
            warning => warning.Title == "启动明日预约失败" &&
                       warning.Message.Contains("明日预约触发时间", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartGrabAsync_UsesSelectedTimePickerValue_ForScheduledStart()
    {
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync();
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.IsGrabScheduledStartEnabled = true;
        viewModel.ScheduledStartTime = new TimeSpan(7, 59, 55);

        await viewModel.StartGrabCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GrabSeatPlan>(coordinator.LastPlan);
        Assert.Equal(new TimeOnly(7, 59, 55), plan.ScheduledStart);
    }

    [Fact]
    public async Task StartGrabAsync_TreatsMidnightTimePickerValue_AsScheduledStart_WhenTimedStartEnabled()
    {
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync();
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.IsGrabScheduledStartEnabled = true;
        viewModel.ScheduledStartTime = TimeSpan.Zero;

        await viewModel.StartGrabCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GrabSeatPlan>(coordinator.LastPlan);
        Assert.Equal(TimeOnly.MinValue, plan.ScheduledStart);
    }

    [Fact]
    public async Task StartGrabAsync_UsesCurrentDefault_WhenTimePickerClearedAndTimedStartEnabled()
    {
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync();
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.IsGrabScheduledStartEnabled = true;
        viewModel.ScheduledStartTime = new TimeSpan(7, 59, 55);

        viewModel.ScheduledStartTime = null;
        await viewModel.StartGrabCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GrabSeatPlan>(coordinator.LastPlan);
        Assert.Equal((TimeSpan?)new TimeSpan(7, 59, 55), viewModel.ScheduledStartTime);
        Assert.Equal(new TimeOnly(7, 59, 55), plan.ScheduledStart);
    }

    [Fact]
    public async Task StartGrabAsync_IgnoresTimePickerValue_WhenTimedStartDisabled()
    {
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync();
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.IsGrabScheduledStartEnabled = false;
        viewModel.ScheduledStartTime = new TimeSpan(7, 59, 55);

        await viewModel.StartGrabCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GrabSeatPlan>(coordinator.LastPlan);
        Assert.Null(plan.ScheduledStart);
    }

    [Fact]
    public async Task StartGrabAsync_ShowsWarning_WhenScheduledStartTimeOutOfRange()
    {
        var notifications = new FakeNotificationService();
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync(notificationService: notifications);
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.IsGrabScheduledStartEnabled = true;
        viewModel.ScheduledStartTime = TimeSpan.FromDays(1);

        await viewModel.StartGrabCommand.ExecuteAsync(null);

        Assert.Null(coordinator.LastPlan);
        Assert.Contains(
            notifications.Warnings,
            warning => warning.Title == "启动抢座失败" &&
                       warning.Message.Contains("抢座定时启动时间", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTomorrowReservationNowAsync_UsesLockedLibrary_WhenSelectedLibraryDrifts()
    {
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.SelectedLibrary = new LibrarySummary(999001, "临时预览场馆", "1层", true, 10, 1, 0);

        await viewModel.RunTomorrowReservationNowCommand.ExecuteAsync(null);

        var plan = Assert.IsType<TomorrowReservationPlan>(coordinator.LastPlan);
        Assert.Equal(117580, plan.LibraryId);
        Assert.Equal("自科阅览区一", plan.LibraryName);
        Assert.Equal(new SeatReference("seat-1", "1"), plan.Seat);
    }

    [Fact]
    public async Task RunTomorrowReservationNowAsync_Blocks_WhenVenuePreviewIsActive()
    {
        var notifications = new FakeNotificationService();
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync(notificationService: notifications);
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.HasActiveVenuePreview = true;

        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        await viewModel.RunTomorrowReservationNowCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanEditTomorrowConfiguration);
        Assert.Null(coordinator.LastPlan);
        Assert.Contains(notifications.Warnings, warning => warning.Message == "请先锁定当前预览场馆后再进行明日预约");
    }

    [Fact]
    public async Task RunTomorrowReservationNowAsync_ClearsSeat_WhenSelectedSeatIsNotInLockedLayout()
    {
        var notifications = new FakeNotificationService();
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync(notificationService: notifications);
        viewModel.SelectedTomorrowSeat = new SeatReference("missing-seat", "不存在的座位");

        await viewModel.RunTomorrowReservationNowCommand.ExecuteAsync(null);

        Assert.Null(coordinator.LastPlan);
        Assert.Null(viewModel.SelectedTomorrowSeat);
        Assert.Contains(notifications.Warnings, warning => warning.Message == "请重新选择明日预约目标座位");
    }

    [Fact]
    public async Task RunTomorrowReservationNowAsync_RefreshesVerificationText_WhenNewTaskStarts()
    {
        var (viewModel, _) = await CreateBoundTomorrowViewModelAsync();
        await viewModel.OpenTomorrowSeatSelectionOverlayCommand.ExecuteAsync(null);
        viewModel.TomorrowVisibleSeats[0].IsSelected = true;
        viewModel.ConfirmTomorrowSeatSelectionCommand.Execute(null);
        viewModel.TomorrowVerificationText = "上一次明日预约成功";

        await viewModel.RunTomorrowReservationNowCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual("上一次明日预约成功", viewModel.TomorrowVerificationText);
        Assert.Equal("明日预约任务已启动，等待结果", viewModel.TomorrowVerificationText);
    }

    [Fact]
    public async Task StopTomorrowReservationAsync_StopsCoordinator()
    {
        var (viewModel, coordinator) = await CreateBoundTomorrowViewModelAsync();

        await viewModel.StopTomorrowReservationCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.StopCalls);
    }

    [Fact]
    public async Task TomorrowReservationSuccessMetrics_UseStatusReason_NotMessageText()
    {
        var settingsService = new FakeSettingsService(WithDashboard(0, 0));
        var tomorrowCoordinator = new FakeTomorrowReservationCoordinator();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            tomorrowReservationCoordinator: tomorrowCoordinator);
        await viewModel.InitializeAsync();

        tomorrowCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "明日预约",
            "预约流程完成",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.TomorrowReservationSucceeded));
        Dispatcher.UIThread.RunJobs();
        await WaitForAsync(() => settingsService.SaveCalls > 0);

        Assert.Equal(1, viewModel.HomeHistoricalSuccessCount);
        Assert.Equal(1, settingsService.CurrentSettings.Dashboard.SuccessfulReservationCount);
    }

    [Fact]
    public async Task TomorrowReservationRunningStatus_UpdatesVerificationText()
    {
        var tomorrowCoordinator = new FakeTomorrowReservationCoordinator();
        var viewModel = CreateViewModel(tomorrowReservationCoordinator: tomorrowCoordinator);
        await viewModel.InitializeAsync();

        tomorrowCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "明日预约",
            "正在提交明日预约",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("正在提交明日预约", viewModel.TomorrowVerificationText);
    }

    [Fact]
    public async Task SaveProtocolOverridesAsync_IncludesTomorrowReservationTemplates()
    {
        var protocolTemplateStore = new FakeProtocolTemplateStore(new TraceIntGraphQlTemplates(
            "cookie-default",
            "libraries-default",
            "layout-default",
            "rule-default",
            "reservation-default",
            "reserve-default",
            "cancel-default",
            "queue-default",
            "warm-default",
            "save-default",
            "info-default"));
        var viewModel = CreateViewModel(protocolTemplateStore: protocolTemplateStore);
        viewModel.TomorrowReservationQueueUrlTemplateText = "queue-override";
        viewModel.TomorrowReservationWarmUpTemplateText = "warm-override";
        viewModel.TomorrowReservationSaveTemplateText = "save-override";
        viewModel.TomorrowReservationInfoTemplateText = "info-override";

        await viewModel.SaveProtocolOverridesCommand.ExecuteAsync(null);

        Assert.Equal(1, protocolTemplateStore.SaveCalls);
        Assert.NotNull(protocolTemplateStore.LastOverrides);
        Assert.Equal("queue-override", protocolTemplateStore.LastOverrides.TomorrowReservationQueueUrlTemplate);
        Assert.Equal("warm-override", protocolTemplateStore.LastOverrides.TomorrowReservationWarmUpTemplate);
        Assert.Equal("save-override", protocolTemplateStore.LastOverrides.TomorrowReservationSaveTemplate);
        Assert.Equal("info-override", protocolTemplateStore.LastOverrides.TomorrowReservationInfoTemplate);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeSessionService? sessionService = null,
        FakeLibraryService? libraryService = null,
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeGrabSeatCoordinator? grabSeatCoordinator = null,
        FakeOccupySeatCoordinator? occupySeatCoordinator = null,
        FakeTomorrowReservationCoordinator? tomorrowReservationCoordinator = null,
        FakeNotificationService? notificationService = null,
        FakeTaskEventAlertDispatcher? taskAlertService = null,
        FakeErrorDialogService? errorDialogService = null,
        FakeAppThemeService? appThemeService = null,
        ActivityLogService? activityLogService = null,
        FakeProtocolTemplateStore? protocolTemplateStore = null)
    {
        sessionService ??= new FakeSessionService();
        libraryService ??= new FakeLibraryService();
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        apiClient ??= new FakeTraceIntApiClient();
        grabSeatCoordinator ??= new FakeGrabSeatCoordinator();
        occupySeatCoordinator ??= new FakeOccupySeatCoordinator();
        tomorrowReservationCoordinator ??= new FakeTomorrowReservationCoordinator();
        taskAlertService ??= new FakeTaskEventAlertDispatcher();
        activityLogService ??= new ActivityLogService();

        return new MainWindowViewModel(
            new SessionWorkflowService(apiClient, sessionService),
            new VenueWorkflowService(libraryService, sessionService, apiClient, settingsService),
            new ReservationWorkflowService(sessionService, apiClient, occupySeatCoordinator, activityLogService),
            new SettingsWorkflowService(settingsService),
            new ProtocolTemplateEditorService(protocolTemplateStore ?? new FakeProtocolTemplateStore(new TraceIntGraphQlTemplates("", "", "", "", "", "", ""))),
            taskAlertService,
            grabSeatCoordinator,
            occupySeatCoordinator,
            tomorrowReservationCoordinator,
            activityLogService,
            notificationService ?? new FakeNotificationService(),
            errorDialogService ?? new FakeErrorDialogService(),
            appThemeService ?? new FakeAppThemeService(),
            new AppWindowService());
    }

    private static async Task<(MainWindowViewModel ViewModel, FakeTomorrowReservationCoordinator Coordinator)>
        CreateBoundTomorrowViewModelAsync(
            FakeTomorrowReservationCoordinator? coordinator = null,
            FakeNotificationService? notificationService = null)
    {
        var grabCoordinator = new FakeGrabSeatCoordinator();
        var result = await CreateBoundSeatViewModelAsync(
            grabCoordinator: grabCoordinator,
            tomorrowCoordinator: coordinator,
            notificationService: notificationService);

        return (result.ViewModel, result.TomorrowCoordinator);
    }

    private static async Task<(MainWindowViewModel ViewModel, FakeGrabSeatCoordinator Coordinator)>
        CreateBoundGrabViewModelAsync(
            FakeGrabSeatCoordinator? coordinator = null,
            FakeNotificationService? notificationService = null)
    {
        coordinator ??= new FakeGrabSeatCoordinator();
        var result = await CreateBoundSeatViewModelAsync(
            grabCoordinator: coordinator,
            notificationService: notificationService);

        return (result.ViewModel, coordinator);
    }

    private static async Task<(
        MainWindowViewModel ViewModel,
        FakeGrabSeatCoordinator GrabCoordinator,
        FakeTomorrowReservationCoordinator TomorrowCoordinator)>
        CreateBoundSeatViewModelAsync(
            FakeGrabSeatCoordinator? grabCoordinator = null,
            FakeTomorrowReservationCoordinator? tomorrowCoordinator = null,
            FakeNotificationService? notificationService = null)
    {
        var library = new LibrarySummary(117580, "自科阅览区一", "3层", true, 120, 20, 10);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = [library]
        };
        libraryService.LayoutsByLibraryId[library.LibraryId] = new LibraryLayout(
            library.LibraryId,
            library.Name,
            library.Floor,
            library.IsOpen,
            120,
            10,
            20,
            [
                new SeatSnapshot("seat-1", "1", false, 0, 0),
                new SeatSnapshot("seat-2", "2", true, 1, 0),
                new SeatSnapshot("seat-3", "3", false, 2, 0)
            ]);
        grabCoordinator ??= new FakeGrabSeatCoordinator();
        tomorrowCoordinator ??= new FakeTomorrowReservationCoordinator();
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            grabSeatCoordinator: grabCoordinator,
            tomorrowReservationCoordinator: tomorrowCoordinator,
            notificationService: notificationService);

        viewModel.IsAuthorized = true;
        viewModel.SelectedLibrary = library;
        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);

        return (viewModel, grabCoordinator, tomorrowCoordinator);
    }

    private static AppSettings WithVenue(int? libraryId, string? libraryName)
        => AppSettings.Default with
        {
            Venue = new VenueSelectionSettings(libraryId, libraryName)
        };

    private static AppSettings WithDashboard(int successfulReservationCount, long totalGuardSeconds)
        => AppSettings.Default with
        {
            Dashboard = new DashboardMetrics(successfulReservationCount, totalGuardSeconds)
        };

    private static AppSettings WithTaskEventAlerts(TaskEventAlertSettings alerts)
        => AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = alerts
            }
        };

    private static string BuildAuthorizationCookie(DateTimeOffset expiresAt)
    {
        var header = Base64Url("""{"typ":"JWT","alg":"RS256"}""");
        var payload = Base64Url($$"""{"userId":37580434,"schId":20175,"expireAt":{{expiresAt.ToUnixTimeSeconds()}},"tag":"cookie-test"}""");
        return $"Authorization={header}.{payload}.signature; SERVERID=d3936289adfff6c3874a2579058ac651|1777956374|1777956374";
    }

    private static string Base64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static Color GetBrushColor(IBrush brush)
    {
        return Assert.IsType<SolidColorBrush>(brush).Color;
    }

    private static AppSettings CreateDesktopDefaultSettings()
    {
        return AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                Theme = new ThemePreferences(AppThemeMode.FollowSystem, true)
            }
        };
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
