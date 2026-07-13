using System.Net;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.Updates;
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
    public async Task MobileControlCookieRefresh_UpdatesSessionStateWithoutSwitchingTabOrShowingSuccessToast()
    {
        var code = "1234567890abcdef1234567890abcdef";
        var cookie = BuildAuthorizationCookie(DateTimeOffset.Now.AddHours(2));
        var notificationService = new FakeNotificationService();
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad =
            [
                new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10),
                new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5)
            ]
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult(cookie)
        };
        var viewModel = CreateViewModel(
            libraryService: libraryService,
            apiClient: apiClient,
            notificationService: notificationService);
        await viewModel.InitializeAsync();
        viewModel.SelectedTabIndex = 0;

        var result = await viewModel.Session.ParseCookieFromLinkAsync(
            $"https://example.test/auth?code={code}",
            SessionCookieLinkParseOptions.MobileControlRefresh);

        Assert.True(result.Authenticated);
        Assert.True(viewModel.IsAuthorized);
        Assert.Equal(cookie, viewModel.ManualCookieText);
        Assert.Equal(cookie, viewModel.WorkflowState.CurrentCookie);
        Assert.True(viewModel.HasCurrentCookie);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);
        Assert.Equal(0, viewModel.SelectedTabIndex);
        Assert.Empty(notificationService.Successes);
    }

    [Fact]
    public async Task InitializeAsync_WithMobileControlAutoStartAndNoSession_StartsMobileControl()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token", true)
        });
        var mobileControlService = new FakeMobileControlService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            mobileControlService: mobileControlService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.IsAuthorized);
        Assert.True(viewModel.IsMobileControlRunning);
        Assert.Equal(1, mobileControlService.StartCalls);
        Assert.Contains("http://127.0.0.1:9527/?token=token", viewModel.MobileControlUrlText);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_WithMobileControlAutoStart_DoesNotStartMobileControlAgain()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(9527, "token", true)
        });
        var mobileControlService = new FakeMobileControlService();
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            mobileControlService: mobileControlService);
        await viewModel.InitializeAsync();
        Assert.Equal(1, mobileControlService.StartCalls);

        viewModel.ManualCookieText = BuildAuthorizationCookie(DateTimeOffset.Now.AddHours(2));
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Equal(1, mobileControlService.StartCalls);
        Assert.True(viewModel.IsMobileControlRunning);
    }

    [Fact]
    public async Task MobileControlCookieRefresh_WhenValidationFails_PreservesCurrentSessionPresentation()
    {
        var firstCode = "1234567890abcdef1234567890abcdef";
        var secondCode = "abcdef1234567890abcdef1234567890";
        var originalCookie = BuildAuthorizationCookie(DateTimeOffset.Now.AddHours(2));
        var failedCookie = BuildAuthorizationCookie(DateTimeOffset.Now.AddHours(3));
        var sessionService = new FakeSessionService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult(originalCookie)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient);
        await viewModel.InitializeAsync();
        var firstResult = await viewModel.Session.ParseCookieFromLinkAsync(
            $"https://example.test/auth?code={firstCode}",
            SessionCookieLinkParseOptions.MobileControlRefresh);
        Assert.True(firstResult.Authenticated);
        var originalSessionSummary = viewModel.SessionSummary;

        apiClient.OnGetCookieFromCodeAsync = (_, _) => Task.FromResult(failedCookie);
        sessionService.AuthenticateFromCookieException = new InvalidOperationException("invalid cookie");
        var secondResult = await viewModel.Session.ParseCookieFromLinkAsync(
            $"https://example.test/auth?code={secondCode}",
            SessionCookieLinkParseOptions.MobileControlRefresh);

        Assert.False(secondResult.Authenticated);
        Assert.Equal(SessionCookieLinkParseStatus.AuthenticationFailed, secondResult.Status);
        Assert.True(viewModel.IsAuthorized);
        Assert.Equal(originalCookie, viewModel.ManualCookieText);
        Assert.Equal(originalCookie, viewModel.WorkflowState.CurrentCookie);
        Assert.Equal(originalSessionSummary, viewModel.SessionSummary);

        sessionService.AuthenticateFromCookieException = null;
        var retryResult = await viewModel.Session.ParseCookieFromLinkAsync(
            $"https://example.test/auth?code={secondCode}",
            SessionCookieLinkParseOptions.MobileControlRefresh);

        Assert.True(retryResult.Authenticated);
        Assert.Equal(failedCookie, viewModel.ManualCookieText);
        Assert.Equal(failedCookie, viewModel.WorkflowState.CurrentCookie);
    }

    [Fact]
    public void SidebarItems_ExposeSystemSettings_WhenUnauthorized()
    {
        var viewModel = CreateViewModel();

        var titles = viewModel.SidebarItems.Select(item => item.Title).ToArray();
        var pageIndexes = viewModel.SidebarItems.Select(item => item.PageIndex).ToArray();

        Assert.Equal(["首页", "账户与场馆", "系统设置"], titles);
        Assert.Equal([0, 1, 9], pageIndexes);
    }

    [Fact]
    public async Task InitializeAsync_UpdatesHomeDashboardDateAndTime()
    {
        var observedAt = CreateLocalTimestamp(2026, 7, 6, 22, 1, 46);
        var timeProvider = new FakeTimeProvider(observedAt);
        var viewModel = CreateViewModel(timeProvider: timeProvider);

        await viewModel.InitializeAsync();

        Assert.Equal(
            observedAt.ToLocalTime().ToString("yyyy 年 MM 月 dd 日 dddd", CultureInfo.GetCultureInfo("zh-CN")),
            viewModel.HomeDateText);
        Assert.Equal("22:01:46", viewModel.HomeTimeText);
        Assert.Equal(viewModel.HomeDateText, viewModel.HomeDashboard.HomeDateText);
        Assert.Equal(viewModel.HomeTimeText, viewModel.HomeDashboard.HomeTimeText);
    }

    [Fact]
    public void SelectedTabIndex_AllowsSystemSettings_WhenUnauthorized()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedTabIndex = 9;

        Assert.Equal(9, viewModel.SelectedTabIndex);

        viewModel.SelectedTabIndex = 2;

        Assert.Equal(1, viewModel.SelectedTabIndex);
    }

    [Fact]
    public void ModalOverlayState_DisablesSidebarNavigation_ForEveryInWindowDialog()
    {
        var viewModel = CreateViewModel();
        Action<bool>[] setModalStates =
        [
            value => viewModel.IsLanCookieRelayDialogOpen = value,
            value => viewModel.IsGrabSeatSelectionOverlayOpen = value,
            value => viewModel.IsMobileControlDetailsOpen = value,
            value => viewModel.IsGlobalLeakLibraryPickerOpen = value,
            value => viewModel.IsTomorrowSeatSelectionOverlayOpen = value,
            value => viewModel.IsVenuePickerOpen = value
        ];

        Assert.False(viewModel.HasOpenModalOverlay);
        Assert.True(viewModel.IsSidebarNavigationInteractive);

        foreach (var setModalState in setModalStates)
        {
            setModalState(true);

            Assert.True(viewModel.HasOpenModalOverlay);
            Assert.False(viewModel.IsSidebarNavigationInteractive);

            setModalState(false);

            Assert.False(viewModel.HasOpenModalOverlay);
            Assert.True(viewModel.IsSidebarNavigationInteractive);
        }
    }

    [AvaloniaFact]
    public async Task OpenModalOverlay_BlocksSidebarPointerInputWithoutDimmingIt()
    {
        var viewModel = CreateViewModel();
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

            var sidebar = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("SidebarNavigationList"));

            Assert.True(sidebar.IsEnabled);
            Assert.True(sidebar.IsHitTestVisible);

            viewModel.IsGrabSeatSelectionOverlayOpen = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(sidebar.IsEnabled);
            Assert.False(sidebar.IsHitTestVisible);
            Assert.Equal(2, viewModel.SelectedTabIndex);

            var modal = Assert.IsType<Border>(
                window.FindControl<Border>("GrabSeatSelectionModal"));
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

    [Fact]
    public void SidebarItems_ExposeRestrictedEntries_WhenAuthorized()
    {
        var viewModel = CreateViewModel();

        viewModel.IsAuthorized = true;

        var titles = viewModel.SidebarItems.Select(item => item.Title).ToArray();
        var pageIndexes = viewModel.SidebarItems.Select(item => item.PageIndex).ToArray();

        Assert.Equal(["首页", "账户与场馆", "抢座", "全域捡漏", "明日预约", "占座", "远程签到", "手机控制", "自动通知", "系统设置"], titles);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], pageIndexes);
    }

    [Fact]
    public void SystemSettingsCategories_ExposeSettingsCenterBuckets()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(["常规", "外观", "网络与接口", "存储与日志", "关于"], viewModel.SystemSettingsCategories);
        Assert.True(viewModel.IsSystemSettingsGeneralActive);

        viewModel.SelectedSystemSettingsCategoryIndex = 2;

        Assert.False(viewModel.IsSystemSettingsGeneralActive);
        Assert.True(viewModel.IsSystemSettingsNetworkActive);

        viewModel.SelectedSystemSettingsCategoryIndex = 3;

        Assert.True(viewModel.IsSystemSettingsStorageActive);
        Assert.False(viewModel.IsSystemSettingsAboutActive);

        viewModel.SelectedSystemSettingsCategoryIndex = 4;

        Assert.False(viewModel.IsSystemSettingsStorageActive);
        Assert.True(viewModel.IsSystemSettingsAboutActive);
    }

    [Fact]
    public async Task InitializeAsync_CloudflareModeUpdatesAllQuickTransferLabels()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                NetworkMode: MobileControlNetworkMode.CloudflareTunnel)
        });
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.Equal(1, viewModel.SystemSettings.SelectedMobileControlNetworkModeIndex);
        Assert.Equal("公网快传", viewModel.SystemSettings.CookieQuickTransferButtonText);
        Assert.Equal("公网快传签到授权", viewModel.SystemSettings.RemoteCheckInQuickTransferButtonText);
    }

    [Fact]
    public async Task CloudflareProxySettings_LoadAndApplyManualProxy()
    {
        var exposureManager = new FakeNetworkExposureManager();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                TunnelProxyMode: CloudflareTunnelProxyMode.SystemProxy)
        });
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            networkExposureManager: exposureManager);
        await viewModel.InitializeAsync();

        Assert.Equal((int)CloudflareTunnelProxyMode.SystemProxy,
            viewModel.SystemSettings.SelectedCloudflareTunnelProxyModeIndex);

        viewModel.SystemSettings.SelectedCloudflareTunnelProxyModeIndex =
            (int)CloudflareTunnelProxyMode.ManualHttpProxy;
        viewModel.SystemSettings.CloudflareTunnelManualProxyUrl = "http://127.0.0.1:7897";
        await viewModel.SystemSettings.ApplyCloudflareTunnelProxySettingsCommand.ExecuteAsync(null);

        Assert.True(viewModel.SystemSettings.IsManualCloudflareTunnelProxy);
        Assert.Equal(CloudflareTunnelProxyMode.ManualHttpProxy, exposureManager.TunnelProxyMode);
        Assert.Equal("http://127.0.0.1:7897", exposureManager.TunnelManualProxyUrl);
    }

    [Fact]
    public async Task CloudflareFallbackSetting_LoadsAndAppliesImmediately()
    {
        var exposureManager = new FakeNetworkExposureManager();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                NetworkMode: MobileControlNetworkMode.CloudflareTunnel,
                FallbackToLocalNetworkOnTunnelFailure: false)
        });
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            networkExposureManager: exposureManager);
        await viewModel.InitializeAsync();

        Assert.False(viewModel.SystemSettings.FallbackToLocalNetworkOnTunnelFailure);
        Assert.False(exposureManager.FallbackToLocalNetworkOnTunnelFailure);

        viewModel.SystemSettings.FallbackToLocalNetworkOnTunnelFailure = true;
        await WaitForAsync(() => exposureManager.FallbackToLocalNetworkOnTunnelFailure);

        Assert.True(viewModel.SystemSettings.IsCloudflareTunnelSelected);
    }

    [Fact]
    public async Task ClashMihomoCompatibilitySettings_LoadAndApplyGenericConfiguration()
    {
        var exposureManager = new FakeNetworkExposureManager();
        var initialPath = Path.GetFullPath("initial-mihomo.yaml");
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            MobileControl = new MobileControlSettings(
                9527,
                "token",
                ClashMihomoCompatibilityEnabled: true,
                ClashMihomoConfigPath: initialPath,
                ClashMihomoRoutePolicy: "Initial Group")
        });
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            networkExposureManager: exposureManager);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.SystemSettings.ClashMihomoCompatibilityEnabled);
        Assert.Equal(initialPath, viewModel.SystemSettings.ClashMihomoConfigPath);
        Assert.Equal("Initial Group", viewModel.SystemSettings.ClashMihomoRoutePolicy);

        var updatedPath = Path.GetFullPath("custom-mihomo.yaml");
        viewModel.SystemSettings.ClashMihomoConfigPath = updatedPath;
        viewModel.SystemSettings.ClashMihomoRoutePolicy = "Cloudflare 专线";
        await viewModel.SystemSettings.ApplyClashMihomoCompatibilitySettingsCommand.ExecuteAsync(null);

        Assert.True(exposureManager.ClashMihomoCompatibilityEnabled);
        Assert.Equal(updatedPath, exposureManager.ClashMihomoConfigPath);
        Assert.Equal("Cloudflare 专线", exposureManager.ClashMihomoRoutePolicy);
    }

    [Fact]
    public void NotificationSettingsCategories_ExposeTabStripItems()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(["通知事件开关", "邮件提醒配置", "Telegram Bot 配置", "Server酱配置", "WxPusher 推送配置", "Bark 推送配置", "弹窗提醒配置"], viewModel.NotificationSettingsCategories);
    }

    [Fact]
    public async Task StartGlobalLeakAsync_BuildsMultiLibraryPlan_WithDefaultScanInterval()
    {
        var coordinator = new FakeGlobalLeakCoordinator();
        var viewModel = CreateGlobalLeakViewModel(globalLeakCoordinator: coordinator);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        viewModel.GlobalLeakLibraries[2].IsSelected = true;
        Assert.True(viewModel.MoveDraftGlobalLeakLibrary(3, 1, insertAfter: false));
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);

        await viewModel.StartGlobalLeakCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GlobalLeakPlan>(coordinator.LastPlan);
        Assert.Equal(TimeSpan.FromSeconds(10), plan.ScanInterval);
        Assert.Equal([3, 1], plan.Libraries.Select(library => library.LibraryId).ToArray());
        Assert.Equal(["场馆C", "场馆A"], plan.Libraries.Select(library => library.LibraryName).ToArray());
    }

    [Fact]
    public async Task GlobalLeakLibraryPicker_UsesDraftSelection_UntilConfirmed()
    {
        var viewModel = CreateGlobalLeakViewModel();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;

        Assert.True(viewModel.IsGlobalLeakLibraryPickerOpen);
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);
        Assert.Equal("本次已勾选 1 个场馆，右侧从上到下依次扫描", viewModel.DraftGlobalLeakLibrarySummaryText);

        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);

        Assert.False(viewModel.IsGlobalLeakLibraryPickerOpen);
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.SelectAllGlobalLeakLibrariesCommand.Execute(null);
        viewModel.ClearDraftGlobalLeakLibrariesCommand.Execute(null);

        Assert.Equal("本次尚未勾选场馆", viewModel.DraftGlobalLeakLibrarySummaryText);
    }

    [Fact]
    public async Task GlobalLeakRunning_DisablesConfiguration_AndStopCallsCoordinator()
    {
        var coordinator = new FakeGlobalLeakCoordinator();
        var viewModel = CreateGlobalLeakViewModel(globalLeakCoordinator: coordinator);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        viewModel.ConfirmGlobalLeakLibrariesCommand.Execute(null);
        await viewModel.StartGlobalLeakCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.IsGlobalLeakTaskActive);
        Assert.False(viewModel.CanEditGlobalLeakConfiguration);
        Assert.True(viewModel.ShouldHideToTrayOnClose);

        await viewModel.StopGlobalLeakCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, coordinator.StopCalls);
        Assert.False(viewModel.IsGlobalLeakTaskActive);
    }

    [Fact]
    public async Task GlobalLeakStatusAndLogs_UpdateDashboardFields()
    {
        var coordinator = new FakeGlobalLeakCoordinator();
        var activityLogService = new ActivityLogService();
        var viewModel = CreateGlobalLeakViewModel(
            globalLeakCoordinator: coordinator,
            activityLogService: activityLogService);
        await viewModel.InitializeAsync();

        var timestamp = DateTimeOffset.Now.AddSeconds(-3);
        coordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "全域捡漏",
            "第 2 轮扫描中",
            timestamp,
            timestamp,
            PollCount: 2,
            RequestCount: 7,
            LastRequestAt: timestamp,
            Reason: CoordinatorStatusReason.Running));
        activityLogService.Write(LogEntryKind.Info, "GlobalLeak", "场馆A 暂无空座。");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("第 2 轮扫描中", viewModel.GlobalLeakStatusText);
        Assert.True(viewModel.IsGlobalLeakTaskActive);
        Assert.Equal(2, viewModel.GlobalLeakScanRoundCount);
        Assert.Equal(7, viewModel.GlobalLeakRequestCount);
        Assert.NotEqual("无", viewModel.GlobalLeakLastRequestText);
        Assert.Contains("GlobalLeak: 场馆A 暂无空座", viewModel.GlobalLeakLogsText);
        Assert.Contains("全域捡漏运行中", viewModel.HomeEngineSummaryText);
    }

    [Fact]
    public async Task GlobalLeakSuccess_RefreshesReservation_AndRecordsDashboardMetrics()
    {
        var settingsService = new FakeSettingsService(WithDashboard(0, 0));
        var coordinator = new FakeGlobalLeakCoordinator();
        var reservationInfoCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                reservationInfoCalls++;
                return Task.FromResult<ReservationInfo?>(new ReservationInfo(
                    "reservation-token",
                    1,
                    "场馆A",
                    "seat-1",
                    "001",
                    DateTimeOffset.Now.AddMinutes(30)));
            }
        };
        var viewModel = CreateGlobalLeakViewModel(
            settingsService: settingsService,
            apiClient: apiClient,
            globalLeakCoordinator: coordinator);
        await viewModel.InitializeAsync();

        var timestamp = DateTimeOffset.Now;
        coordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "全域捡漏",
            "已成功捡漏预约到空座",
            timestamp.AddSeconds(-2),
            timestamp,
            Reason: CoordinatorStatusReason.GlobalLeakSucceeded));
        Dispatcher.UIThread.RunJobs();

        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return settingsService.CurrentSettings.Dashboard.SuccessfulReservationCount == 1;
        });
        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return reservationInfoCalls > 0;
        });

        Assert.Equal(1, viewModel.HomeHistoricalSuccessCount);
        Assert.Contains("场馆A", viewModel.ReservationHeroTitle);
    }

    [Fact]
    public async Task InitializeAsync_RestoresStoredGlobalLeakLibraries_WhenSessionRestored()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层"),
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
            new GlobalLeakLibrarySelectionSettings(99, "旧场馆", "旧楼层")));
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.Equal([2, 1], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(["场馆B", "场馆A"], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryName).ToArray());
        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_RestoresStoredGlobalLeakLibraries_AfterFreshAuthorization()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层")));
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

        await viewModel.InitializeAsync();
        Assert.False(viewModel.IsAuthorized);
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);

        viewModel.ManualCookieText = "Authorization=a; SERVERID=b";
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Equal([1], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
    }

    [Fact]
    public async Task ValidateManualCookieAsync_RestoresStoredGlobalLeakLibraries_WhenAlreadyAuthorized()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);

        await settingsService.SaveAsync(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));

        viewModel.ManualCookieText = "Authorization=fresh; SERVERID=b";
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Equal([2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
    }

    [Fact]
    public async Task ValidateManualCookieAsync_RetriesGlobalLeakRestore_AfterSettingsLoadFailure()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));
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
        await viewModel.InitializeAsync();

        settingsService.LoadExceptions.Enqueue(new InvalidOperationException("database busy"));
        viewModel.ManualCookieText = "Authorization=a; SERVERID=b";
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);

        await viewModel.LoadLibrariesCommand.ExecuteAsync(null);

        Assert.Equal([2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
    }

    [Fact]
    public async Task GlobalLeakLibraryPicker_CancelDoesNotPersistDraftSelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);

        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);
        Assert.Empty(settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task ConfirmGlobalLeakLibrariesAsync_PersistsSelection()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        viewModel.GlobalLeakLibraries[2].IsSelected = true;
        Assert.True(viewModel.MoveDraftGlobalLeakLibrary(3, 1, insertAfter: false));
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);

        Assert.Equal([3, 1], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1, 2], viewModel.SelectedGlobalLeakLibraryPriorities.Select(x => x.Priority).ToArray());
        Assert.Equal([3, 1], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(1, settingsService.SaveCalls);
    }

    [Fact]
    public async Task ConfirmGlobalLeakLibrariesAsync_CommitsPersistedSnapshot_AndLocksDraftWhileSaving()
    {
        var updateStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();
        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        settingsService.UpdateStarted = updateStarted;
        settingsService.UpdateBlocker = releaseUpdate.Task;

        var confirmTask = viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);
        await updateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.CanEditGlobalLeakConfiguration);
        Assert.False(viewModel.CanCancelGlobalLeakLibraryPicker);
        viewModel.GlobalLeakLibraries[2].IsSelected = true;
        Assert.False(viewModel.MoveDraftGlobalLeakLibrary(3, 1, insertAfter: false));
        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);
        Assert.True(viewModel.IsGlobalLeakLibraryPickerOpen);

        releaseUpdate.TrySetResult(null);
        await confirmTask;

        Assert.False(viewModel.IsGlobalLeakLibraryPickerOpen);
        Assert.True(viewModel.CanEditGlobalLeakConfiguration);
        Assert.True(viewModel.CanCancelGlobalLeakLibraryPicker);
        Assert.Equal([1], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
    }

    [AvaloniaFact]
    public async Task GlobalLeakLibraryPicker_DisablesMutation_WhenTaskBecomesActive()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var coordinator = new FakeGlobalLeakCoordinator();
        var viewModel = CreateGlobalLeakViewModel(
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

    [Fact]
    public async Task GlobalLeakLibraryPriority_CancelRestoresCommittedOrderWithoutSaving()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        Assert.True(viewModel.MoveDraftGlobalLeakLibrary(2, 1, insertAfter: false));
        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);

        Assert.Equal([1, 2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(0, settingsService.SaveCalls);
    }

    [Fact]
    public async Task ConfirmGlobalLeakLibraryPriority_KeepsCommittedOrder_WhenPersistFails()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        Assert.True(viewModel.MoveDraftGlobalLeakLibrary(2, 1, insertAfter: false));
        settingsService.UpdateExceptions.Enqueue(new InvalidOperationException("database locked"));

        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGlobalLeakLibraryPickerOpen);
        Assert.Equal([2, 1], viewModel.DraftGlobalLeakLibraryPriorities.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1, 2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1, 2], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
    }

    [Fact]
    public async Task ConfirmGlobalLeakLibrariesAsync_KeepsDraftOpen_WhenPersistFails()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var notificationService = new FakeNotificationService();
        var viewModel = CreateGlobalLeakViewModel(
            settingsService: settingsService,
            notificationService: notificationService);
        await viewModel.InitializeAsync();

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        settingsService.UpdateExceptions.Enqueue(new InvalidOperationException("database locked"));

        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsGlobalLeakLibraryPickerOpen);
        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);
        Assert.Empty(settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.Contains(notificationService.Warnings, warning => warning.Title == "保存扫描场馆失败");
    }

    [Fact]
    public async Task RemoveAndClearSelectedGlobalLeakLibraries_PersistSelection()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));
        var viewModel = CreateGlobalLeakViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        await viewModel.RemoveSelectedGlobalLeakLibraryCommand.ExecuteAsync(viewModel.SelectedGlobalLeakLibraries[0]);

        Assert.Equal([2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([2], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());

        await viewModel.ClearGlobalLeakLibrarySelectionCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedGlobalLeakLibraries);
        Assert.Empty(settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries);
        Assert.Equal(2, settingsService.SaveCalls);
    }

    [Fact]
    public async Task RemoveAndClearSelectedGlobalLeakLibraries_KeepSelection_WhenPersistFails()
    {
        var settingsService = new FakeSettingsService(WithGlobalLeakSelectedLibraries(
            new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
            new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层")));
        var notificationService = new FakeNotificationService();
        var viewModel = CreateGlobalLeakViewModel(
            settingsService: settingsService,
            notificationService: notificationService);
        await viewModel.InitializeAsync();

        settingsService.UpdateExceptions.Enqueue(new InvalidOperationException("remove failed"));
        await viewModel.RemoveSelectedGlobalLeakLibraryCommand.ExecuteAsync(viewModel.SelectedGlobalLeakLibraries[0]);

        Assert.Equal([1, 2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1, 2], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());

        settingsService.UpdateExceptions.Enqueue(new InvalidOperationException("clear failed"));
        await viewModel.ClearGlobalLeakLibrarySelectionCommand.ExecuteAsync(null);

        Assert.Equal([1, 2], viewModel.SelectedGlobalLeakLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal([1, 2], settingsService.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries.Select(x => x.LibraryId).ToArray());
        Assert.Equal(2, notificationService.Warnings.Count(warning => warning.Title == "保存扫描场馆失败"));
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
    public async Task InitializeAsync_LoadsNotificationEventSettings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default with
                {
                    GrabSucceeded = false,
                    TaskFailed = false
                })));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.GrabSucceededAlertsEnabled);
        Assert.True(viewModel.OccupyReReserveSucceededAlertsEnabled);
        Assert.True(viewModel.TomorrowReservationSucceededAlertsEnabled);
        Assert.True(viewModel.GlobalLeakSucceededAlertsEnabled);
        Assert.True(viewModel.SessionInvalidAlertsEnabled);
        Assert.False(viewModel.TaskFailedAlertsEnabled);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveEventAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.GrabSucceededAlertsEnabled = false;
        viewModel.GlobalLeakSucceededAlertsEnabled = false;

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Events.GrabSucceeded == false &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Events.GlobalLeakSucceeded == false);

        var events = Assert.IsType<TaskEventAlertEventSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Events);
        Assert.False(events.GrabSucceeded);
        Assert.True(events.OccupyReReserveSucceeded);
        Assert.True(events.TomorrowReservationSucceeded);
        Assert.False(events.GlobalLeakSucceeded);
        Assert.True(events.SessionInvalid);
        Assert.True(events.TaskFailed);
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
    public async Task InitializeAsync_LoadsServerChanNotificationSettings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                WxPusherAlertChannelSettings.Default,
                new ServerChanAlertChannelSettings(true, "SCT_xxx", true, "9|66", "user-1"))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.ServerChanAlertsEnabled);
        Assert.Equal("SCT_xxx", viewModel.ServerChanAlertSendKey);
        Assert.True(viewModel.ServerChanAlertNoIp);
        Assert.Equal("9|66", viewModel.ServerChanAlertChannel);
        Assert.Equal("user-1", viewModel.ServerChanAlertOpenId);
    }

    [Fact]
    public async Task InitializeAsync_DefaultsNullServerChanNotificationStrings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                WxPusherAlertChannelSettings.Default,
                new ServerChanAlertChannelSettings(true, null!, true, null!, null!))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.ServerChanAlertsEnabled);
        Assert.Equal(string.Empty, viewModel.ServerChanAlertSendKey);
        Assert.True(viewModel.ServerChanAlertNoIp);
        Assert.Equal(string.Empty, viewModel.ServerChanAlertChannel);
        Assert.Equal(string.Empty, viewModel.ServerChanAlertOpenId);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveServerChanAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.ServerChanAlertsEnabled = true;
        viewModel.ServerChanAlertSendKey = " SCT_xxx ";
        viewModel.ServerChanAlertNoIp = true;
        viewModel.ServerChanAlertChannel = " 9|66 ";
        viewModel.ServerChanAlertOpenId = " user-1 ";

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.ServerChan.SendKey == "SCT_xxx");

        var serverChan = Assert.IsType<ServerChanAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.ServerChan);
        Assert.True(serverChan.Enabled);
        Assert.Equal("SCT_xxx", serverChan.SendKey);
        Assert.True(serverChan.NoIp);
        Assert.Equal("9|66", serverChan.Channel);
        Assert.Equal("user-1", serverChan.OpenId);
    }

    [Fact]
    public async Task SendTestServerChanAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeTaskEventAlertDispatcher();
        var viewModel = CreateViewModel(taskAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.ServerChanAlertsEnabled = true;
        viewModel.ServerChanAlertSendKey = " SCT_xxx ";
        viewModel.ServerChanAlertNoIp = true;
        viewModel.ServerChanAlertChannel = " 9|66 ";
        viewModel.ServerChanAlertOpenId = " user-1 ";

        await viewModel.SendTestServerChanAlertCommand.ExecuteAsync(null);

        var request = Assert.Single(alertService.TestServerChanRequests);
        Assert.True(request.Enabled);
        Assert.Equal("SCT_xxx", request.SendKey);
        Assert.True(request.NoIp);
        Assert.Equal("9|66", request.Channel);
        Assert.Equal("user-1", request.OpenId);
    }

    [Fact]
    public async Task SendTestServerChanAlertAsync_ShowsErrorDialog_WhenSendingFails()
    {
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestServerChanException = new InvalidOperationException("serverchan send failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            taskAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.ServerChanAlertSendKey = "SCT_xxx";

        await viewModel.SendTestServerChanAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试 Server酱 发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("serverchan send failed", error.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_LoadsWxPusherNotificationSettings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                new WxPusherAlertChannelSettings(true, "https://wxpusher.example.com", "AT_xxx", "UID_1,UID_2", "1;2"))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.WxPusherAlertsEnabled);
        Assert.Equal("https://wxpusher.example.com", viewModel.WxPusherAlertApiBaseUrl);
        Assert.Equal("AT_xxx", viewModel.WxPusherAlertAppToken);
        Assert.Equal("UID_1,UID_2", viewModel.WxPusherAlertUids);
        Assert.Equal("1;2", viewModel.WxPusherAlertTopicIds);
    }

    [Fact]
    public async Task InitializeAsync_DefaultsNullWxPusherNotificationStrings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                BarkAlertChannelSettings.Default,
                new WxPusherAlertChannelSettings(true, null!, null!, null!, null!))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.WxPusherAlertsEnabled);
        Assert.Equal(WxPusherAlertChannelSettings.DefaultApiBaseUrl, viewModel.WxPusherAlertApiBaseUrl);
        Assert.Equal(string.Empty, viewModel.WxPusherAlertAppToken);
        Assert.Equal(string.Empty, viewModel.WxPusherAlertUids);
        Assert.Equal(string.Empty, viewModel.WxPusherAlertTopicIds);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveWxPusherAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.WxPusherAlertsEnabled = true;
        viewModel.WxPusherAlertApiBaseUrl = "https://wxpusher.example.com/";
        viewModel.WxPusherAlertAppToken = " AT_xxx ";
        viewModel.WxPusherAlertUids = " UID_1, UID_2 ";
        viewModel.WxPusherAlertTopicIds = " 1;2 ";

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.WxPusher.AppToken == "AT_xxx");

        var wxPusher = Assert.IsType<WxPusherAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.WxPusher);
        Assert.True(wxPusher.Enabled);
        Assert.Equal("https://wxpusher.example.com", wxPusher.ApiBaseUrl);
        Assert.Equal("AT_xxx", wxPusher.AppToken);
        Assert.Equal("UID_1, UID_2", wxPusher.Uids);
        Assert.Equal("1;2", wxPusher.TopicIds);
    }

    [Fact]
    public async Task SendTestWxPusherAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeTaskEventAlertDispatcher();
        var viewModel = CreateViewModel(taskAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.WxPusherAlertsEnabled = true;
        viewModel.WxPusherAlertApiBaseUrl = "https://wxpusher.example.com/";
        viewModel.WxPusherAlertAppToken = " AT_xxx ";
        viewModel.WxPusherAlertUids = " UID_1 ";
        viewModel.WxPusherAlertTopicIds = " 1 ";

        await viewModel.SendTestWxPusherAlertCommand.ExecuteAsync(null);

        var request = Assert.Single(alertService.TestWxPusherRequests);
        Assert.True(request.Enabled);
        Assert.Equal("https://wxpusher.example.com", request.ApiBaseUrl);
        Assert.Equal("AT_xxx", request.AppToken);
        Assert.Equal("UID_1", request.Uids);
        Assert.Equal("1", request.TopicIds);
    }

    [Fact]
    public async Task SendTestWxPusherAlertAsync_ShowsErrorDialog_WhenSendingFails()
    {
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestWxPusherException = new InvalidOperationException("wxpusher send failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            taskAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.WxPusherAlertApiBaseUrl = "https://wxpusher.example.com";
        viewModel.WxPusherAlertAppToken = "AT_xxx";
        viewModel.WxPusherAlertUids = "UID_1";

        await viewModel.SendTestWxPusherAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试 WxPusher 发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("wxpusher send failed", error.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_LoadsBarkNotificationSettings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                new BarkAlertChannelSettings(true, "https://bark.example.com", "key-1", "IGoLibrary-Ex", "alarm", "timeSensitive"))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.BarkAlertsEnabled);
        Assert.Equal("https://bark.example.com", viewModel.BarkAlertApiBaseUrl);
        Assert.Equal("key-1", viewModel.BarkAlertDeviceKey);
        Assert.Equal("IGoLibrary-Ex", viewModel.BarkAlertGroup);
        Assert.Equal("alarm", viewModel.BarkAlertSound);
        Assert.Equal(2, viewModel.SelectedBarkAlertLevelIndex);
    }

    [Fact]
    public async Task InitializeAsync_DefaultsNullBarkNotificationStrings()
    {
        var settingsService = new FakeSettingsService(WithTaskEventAlerts(
            new TaskEventAlertSettings(
                EmailAlertChannelSettings.Default,
                LocalDesktopAlertSettings.Default,
                TelegramAlertChannelSettings.Default,
                TaskEventAlertEventSettings.Default,
                new BarkAlertChannelSettings(true, null!, null!, null!, null!, null!))));
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.True(viewModel.BarkAlertsEnabled);
        Assert.Equal(BarkAlertChannelSettings.DefaultApiBaseUrl, viewModel.BarkAlertApiBaseUrl);
        Assert.Equal(string.Empty, viewModel.BarkAlertDeviceKey);
        Assert.Equal(string.Empty, viewModel.BarkAlertGroup);
        Assert.Equal(string.Empty, viewModel.BarkAlertSound);
        Assert.Equal(0, viewModel.SelectedBarkAlertLevelIndex);
    }

    [Fact]
    public async Task NotificationSettings_AutoSaveBarkAlerts_WhenFieldsChange()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.BarkAlertsEnabled = true;
        viewModel.BarkAlertApiBaseUrl = "https://bark.example.com/";
        viewModel.BarkAlertDeviceKey = " key-1 ";
        viewModel.BarkAlertGroup = " IGoLibrary-Ex ";
        viewModel.BarkAlertSound = " alarm ";
        viewModel.SelectedBarkAlertLevelIndex = 2;

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Bark.DeviceKey == "key-1");

        var bark = Assert.IsType<BarkAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Bark);
        Assert.True(bark.Enabled);
        Assert.Equal("https://bark.example.com", bark.ApiBaseUrl);
        Assert.Equal("key-1", bark.DeviceKey);
        Assert.Equal("IGoLibrary-Ex", bark.Group);
        Assert.Equal("alarm", bark.Sound);
        Assert.Equal("timeSensitive", bark.Level);
    }

    [Fact]
    public async Task SendTestBarkAlertAsync_UsesCurrentNotificationSettingsSnapshot()
    {
        var alertService = new FakeTaskEventAlertDispatcher();
        var viewModel = CreateViewModel(taskAlertService: alertService);
        await viewModel.InitializeAsync();

        viewModel.BarkAlertsEnabled = true;
        viewModel.BarkAlertApiBaseUrl = "https://bark.example.com/";
        viewModel.BarkAlertDeviceKey = " key-1 ";
        viewModel.BarkAlertGroup = " IGoLibrary-Ex ";
        viewModel.BarkAlertSound = " alarm ";
        viewModel.SelectedBarkAlertLevelIndex = 2;

        await viewModel.SendTestBarkAlertCommand.ExecuteAsync(null);

        var request = Assert.Single(alertService.TestBarkRequests);
        Assert.True(request.Enabled);
        Assert.Equal("https://bark.example.com", request.ApiBaseUrl);
        Assert.Equal("key-1", request.DeviceKey);
        Assert.Equal("IGoLibrary-Ex", request.Group);
        Assert.Equal("alarm", request.Sound);
        Assert.Equal("timeSensitive", request.Level);
    }

    [Fact]
    public async Task SendTestBarkAlertAsync_ShowsErrorDialog_WhenSendingFails()
    {
        var alertService = new FakeTaskEventAlertDispatcher
        {
            SendTestBarkException = new InvalidOperationException("bark send failed")
        };
        var errorDialogService = new FakeErrorDialogService();
        var viewModel = CreateViewModel(
            taskAlertService: alertService,
            errorDialogService: errorDialogService);
        await viewModel.InitializeAsync();

        viewModel.BarkAlertApiBaseUrl = "https://bark.example.com";
        viewModel.BarkAlertDeviceKey = "key-1";

        await viewModel.SendTestBarkAlertCommand.ExecuteAsync(null);

        var error = Assert.Single(errorDialogService.Errors);
        Assert.Equal("测试 Bark 发送失败", error.Title);
        Assert.Equal(nameof(InvalidOperationException), error.ErrorType);
        Assert.Equal("bark send failed", error.ErrorMessage);
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
            $"授权链接解析成功，Cookie 已填入{Environment.NewLine}Cookie 到期时间：5月5日 16:56",
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
    public async Task StartLanCookieRelayAsync_OpensDialogAndGeneratesQrCode()
    {
        var relayService = new FakeLanCookieRelayService();
        var qrCodeFactory = new FakeQrCodeImageFactory();
        var viewModel = CreateViewModel(
            lanCookieRelayService: relayService,
            qrCodeImageFactory: qrCodeFactory);

        await viewModel.StartLanCookieRelayCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLanCookieRelayDialogOpen);
        Assert.True(viewModel.IsLanCookieRelayRunning);
        Assert.True(viewModel.ShowLanCookieRelayStartedStatusIcon);
        Assert.Equal("局域网快传已启动，监听端口 49152", viewModel.LanCookieRelayStatusText);
        Assert.True(viewModel.HasLanCookieRelayQrImage);
        Assert.False(viewModel.HasNoLanCookieRelayQrImage);
        Assert.Equal(relayService.NextSession.Url.ToString(), viewModel.LanCookieRelayUrlText);
        Assert.Equal([relayService.NextSession.Url.ToString()], qrCodeFactory.CreatedTexts);
        Assert.Equal(1, relayService.StartCalls);

        viewModel.LanCookieRelayStatusText = "启动文案被调整";
        Assert.True(viewModel.ShowLanCookieRelayStartedStatusIcon);
    }

    [Fact]
    public async Task LanCookieRelaySubmission_WithValidLink_AuthenticatesAndLoadsLibraries()
    {
        var relayService = new FakeLanCookieRelayService();
        var expiresAt = DateTimeOffset.Now.AddHours(2);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetCookieFromCodeAsync = (_, _) => Task.FromResult(BuildAuthorizationCookie(expiresAt))
        };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad =
            [
                new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10)
            ]
        };
        var viewModel = CreateViewModel(
            apiClient: apiClient,
            libraryService: libraryService,
            lanCookieRelayService: relayService);

        await viewModel.InitializeAsync();
        await viewModel.StartLanCookieRelayCommand.ExecuteAsync(null);
        var submitTask = relayService.SubmitAsync("https://example.com/callback?code=1234567890abcdef1234567890abcdef&state=1");
        Dispatcher.UIThread.RunJobs();
        var result = await submitTask;
        Dispatcher.UIThread.RunJobs();

        Assert.True(result.Success);
        Assert.True(viewModel.IsAuthorized);
        Assert.True(viewModel.CanShowVenueConfiguration);
        Assert.False(viewModel.IsLanCookieRelayRunning);
        Assert.False(viewModel.IsLanCookieRelayDialogOpen);
        Assert.Contains("Authorization=", viewModel.ManualCookieText);
        Assert.Equal(["场馆A"], viewModel.AvailableLibraries.Select(library => library.Name).ToArray());
    }

    [Fact]
    public async Task LanCookieRelaySubmission_WithInvalidLink_ReturnsFailureAndDoesNotAuthorize()
    {
        var relayService = new FakeLanCookieRelayService();
        var notificationService = new FakeNotificationService();
        var viewModel = CreateViewModel(
            notificationService: notificationService,
            lanCookieRelayService: relayService);

        await viewModel.InitializeAsync();
        await viewModel.StartLanCookieRelayCommand.ExecuteAsync(null);
        var submitTask = relayService.SubmitAsync("not an authorization link");
        Dispatcher.UIThread.RunJobs();
        var result = await submitTask;
        Dispatcher.UIThread.RunJobs();

        Assert.False(result.Success);
        Assert.False(viewModel.IsAuthorized);
        Assert.False(viewModel.CanShowVenueConfiguration);
        Assert.True(viewModel.IsLanCookieRelayRunning);
        Assert.True(viewModel.IsLanCookieRelayDialogOpen);
        Assert.False(viewModel.ShowLanCookieRelayStartedStatusIcon);
        Assert.Contains("未能从链接中提取", viewModel.LanCookieRelayStatusText);
        var warning = Assert.Single(notificationService.Warnings, item => item.Title == "快传失败");
        Assert.Contains("未能从链接中提取", warning.Message);
    }

    [Fact]
    public async Task StartLanCookieRelayAsync_ProxyConflictShowsExactWarningAndStaysStopped()
    {
        var relayService = new FakeLanCookieRelayService
        {
            StartException = new CloudflareTunnelProxyConflictException()
        };
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            notificationService: notifications,
            lanCookieRelayService: relayService);

        await viewModel.StartLanCookieRelayCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLanCookieRelayRunning);
        Assert.Equal(
            $"快传启动失败：{CloudflareTunnelProxyConflictException.UserMessage}",
            viewModel.LanCookieRelayStatusText);
        var warning = Assert.Single(notifications.Warnings);
        Assert.Equal("快传启动失败", warning.Title);
        Assert.Equal(CloudflareTunnelProxyConflictException.UserMessage, warning.Message);
    }

    [Fact]
    public async Task SignOutAsync_StopsLanCookieRelaySession()
    {
        var relayService = new FakeLanCookieRelayService();
        var viewModel = CreateViewModel(lanCookieRelayService: relayService);

        await viewModel.StartLanCookieRelayCommand.ExecuteAsync(null);
        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(1, relayService.StopCalls);
        Assert.False(viewModel.IsLanCookieRelayRunning);
        Assert.False(viewModel.IsLanCookieRelayDialogOpen);
    }

    [Fact]
    public async Task SignOutAsync_WhenCredentialClearFails_CompletesUiSignOutAndWarns()
    {
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials(
                "cookie",
                SessionSource.ManualCookie,
                DateTimeOffset.Now,
                true),
            SignOutException = new InvalidOperationException("credential delete failed")
        };
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            notificationService: notifications);
        viewModel.IsAuthorized = true;

        await viewModel.SignOutCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAuthorized);
        Assert.Equal("未登录", viewModel.SessionSummary);
        Assert.Contains(
            notifications.Warnings,
            item => item.Title == "已退出，但凭据清理失败");
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
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
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
    public async Task LockedVenueState_SynchronizesToRemoteCheckIn_AcrossPreviewSwitchAndClear()
    {
        var libraryA = new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10);
        var libraryB = new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5);
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
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
        libraryService.LayoutsByLibraryId[libraryB.LibraryId] = new LibraryLayout(
            libraryB.LibraryId,
            libraryB.Name,
            libraryB.Floor,
            libraryB.IsOpen,
            80,
            5,
            10,
            [new SeatSnapshot("seat-2", "2", false, 0, 0)]);

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

        Assert.Equal(libraryA, viewModel.WorkflowState.LockedLibrary);
        Assert.Equal("场馆A · 3层", viewModel.RemoteCheckInPage.LockedLibraryText);

        await viewModel.OpenVenuePickerCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsVenuePickerOpen);
        Assert.Equal(libraryA.LibraryId, viewModel.SelectedLibrary?.LibraryId);
        Assert.Equal(libraryA, viewModel.WorkflowState.LockedLibrary);
        Assert.Equal(1, libraryService.LoadLibrariesCalls);

        viewModel.SelectedLibrary = libraryB;
        await viewModel.BindSelectedLibraryCommand.ExecuteAsync(null);

        Assert.Equal(libraryB, viewModel.WorkflowState.LockedLibrary);
        Assert.Equal("场馆B · 5层", viewModel.RemoteCheckInPage.LockedLibraryText);

        viewModel.AccountVenue.ClearVenueState();

        Assert.Null(viewModel.WorkflowState.LockedLibrary);
        Assert.Equal("尚未锁定场馆", viewModel.RemoteCheckInPage.LockedLibraryText);
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
    public async Task OptimalGrabStrategyReminder_LoadsAndAutoSavesFromGeneralSettings()
    {
        var initial = AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                Grab = AppSettings.Default.Tasks.Grab with
                {
                    OptimalStrategyReminderEnabled = false
                }
            }
        };
        var settingsService = new FakeSettingsService(initial);
        var viewModel = CreateViewModel(settingsService: settingsService);

        await viewModel.InitializeAsync();

        Assert.False(viewModel.SystemSettings.OptimalGrabStrategyReminderEnabled);
        viewModel.SystemSettings.OptimalGrabStrategyReminderEnabled = true;
        await WaitForAsync(() => settingsService.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);

        Assert.True(settingsService.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);
    }

    [Fact]
    public async Task ApplyPersistedOptimalGrabStrategyReminder_UpdatesToggleWithoutSchedulingAnotherSave()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();
        var saveCallsBeforeApply = settingsService.SaveCalls;

        viewModel.SystemSettings.ApplyPersistedOptimalGrabStrategyReminder(false);

        Assert.False(viewModel.SystemSettings.OptimalGrabStrategyReminderEnabled);
        Assert.False(viewModel.SystemSettings.HasPendingAutoSave);
        Assert.True(settingsService.CurrentSettings.Tasks.Grab.OptimalStrategyReminderEnabled);
        Assert.Equal(saveCallsBeforeApply, settingsService.SaveCalls);
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
    public async Task SaveSettingsAsync_PersistsServerChanNotificationSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.ServerChanAlertsEnabled = true;
        viewModel.ServerChanAlertSendKey = " SCT_xxx ";
        viewModel.ServerChanAlertNoIp = true;
        viewModel.ServerChanAlertChannel = " 9|66 ";
        viewModel.ServerChanAlertOpenId = " user-1 ";

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var serverChan = Assert.IsType<ServerChanAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.ServerChan);
        Assert.True(serverChan.Enabled);
        Assert.Equal("SCT_xxx", serverChan.SendKey);
        Assert.True(serverChan.NoIp);
        Assert.Equal("9|66", serverChan.Channel);
        Assert.Equal("user-1", serverChan.OpenId);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsBarkNotificationSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.BarkAlertsEnabled = true;
        viewModel.BarkAlertApiBaseUrl = "https://bark.example.com/";
        viewModel.BarkAlertDeviceKey = " key-1 ";
        viewModel.BarkAlertGroup = " IGoLibrary-Ex ";
        viewModel.BarkAlertSound = " alarm ";
        viewModel.SelectedBarkAlertLevelIndex = 4;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var bark = Assert.IsType<BarkAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Bark);
        Assert.True(bark.Enabled);
        Assert.Equal("https://bark.example.com", bark.ApiBaseUrl);
        Assert.Equal("key-1", bark.DeviceKey);
        Assert.Equal("IGoLibrary-Ex", bark.Group);
        Assert.Equal("alarm", bark.Sound);
        Assert.Equal("critical", bark.Level);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsWxPusherNotificationSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.WxPusherAlertsEnabled = true;
        viewModel.WxPusherAlertApiBaseUrl = "https://wxpusher.example.com/";
        viewModel.WxPusherAlertAppToken = " AT_xxx ";
        viewModel.WxPusherAlertUids = " UID_1 ";
        viewModel.WxPusherAlertTopicIds = " 1 ";

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var wxPusher = Assert.IsType<WxPusherAlertChannelSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.WxPusher);
        Assert.True(wxPusher.Enabled);
        Assert.Equal("https://wxpusher.example.com", wxPusher.ApiBaseUrl);
        Assert.Equal("AT_xxx", wxPusher.AppToken);
        Assert.Equal("UID_1", wxPusher.Uids);
        Assert.Equal("1", wxPusher.TopicIds);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsNotificationEventSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.SessionInvalidAlertsEnabled = false;
        viewModel.TaskFailedAlertsEnabled = false;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        var events = Assert.IsType<TaskEventAlertEventSettings>(settingsService.CurrentSettings.Notifications.TaskEventAlerts?.Events);
        Assert.True(events.GrabSucceeded);
        Assert.True(events.OccupyReReserveSucceeded);
        Assert.True(events.TomorrowReservationSucceeded);
        Assert.True(events.GlobalLeakSucceeded);
        Assert.False(events.SessionInvalid);
        Assert.False(events.TaskFailed);
    }

    [Fact]
    public async Task ThemeSettings_AutoSavePreferences()
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
            settingsService.CurrentSettings.Ui.Theme?.Mode == AppThemeMode.Dark &&
            settingsService.CurrentSettings.Ui.Theme?.UseSystemAccent == false);

        Assert.Equal(AppThemeMode.Dark, settingsService.CurrentSettings.Ui.Theme?.Mode);
        Assert.False(settingsService.CurrentSettings.Ui.Theme?.UseSystemAccent);
        Assert.True(themeService.ApplySettingsCalls >= 2);
        Assert.Equal(AppThemeMode.Dark, themeService.LastAppliedTheme?.Mode);
        Assert.False(themeService.LastAppliedTheme?.UseSystemAccent);
    }

    [Fact]
    public async Task HomeReservationProgressSettings_AutoSavePreferences()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings() with
        {
            Ui = AppSettings.Default.Ui with
            {
                HomeReservationProgress = new HomeReservationProgressSettings(
                    HomeReservationProgressTimingMode.SoftwareRuntimeDuration,
                    40)
            }
        });
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        Assert.Equal(1, viewModel.SelectedHomeReservationProgressTimingModeIndex);
        Assert.Equal(40, viewModel.HomeReservationFixedDurationMinutes);
        Assert.False(viewModel.IsHomeReservationFixedProgressMode);

        viewModel.SelectedHomeReservationProgressTimingModeIndex = 0;
        viewModel.HomeReservationFixedDurationMinutes = 45;

        await WaitForAsync(() =>
            settingsService.CurrentSettings.Ui.HomeReservationProgress?.Mode ==
            HomeReservationProgressTimingMode.FixedReservationDuration &&
            settingsService.CurrentSettings.Ui.HomeReservationProgress.FixedDurationMinutes == 45);

        Assert.True(viewModel.IsHomeReservationFixedProgressMode);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsStartupUpdateCheckPreference()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();
        viewModel.CheckUpdatesOnStartup = false;

        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(settingsService.CurrentSettings.Updates.CheckOnStartup);
    }

    [Fact]
    public async Task InitializeAsync_TriggersAutomaticUpdateCheck()
    {
        var updateCheckService = new FakeUpdateCheckService();
        var viewModel = CreateViewModel(updateCheckService: updateCheckService);

        await viewModel.InitializeAsync();

        Assert.Contains(UpdateCheckMode.Automatic, updateCheckService.CheckModes);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShowsDialogAndSkipsSelectedVersion_WhenUpdateExists()
    {
        var version = new ReleaseVersion(1, 0, 2);
        var release = new ReleaseUpdateInfo(
            version,
            "v1.0.2",
            "IGoLibrary-Ex v1.0.2",
            "更新内容",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.2"),
            new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
            false);
        var updateCheckService = new FakeUpdateCheckService();
        updateCheckService.Results.Enqueue(UpdateCheckResult.UpdateAvailable(release));
        var updateDialogService = new FakeUpdateDialogService
        {
            Result = UpdateDialogResult.SkipVersion
        };
        var viewModel = CreateViewModel(
            updateCheckService: updateCheckService,
            updateDialogService: updateDialogService);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal([UpdateCheckMode.Manual], updateCheckService.CheckModes);
        Assert.Same(release, Assert.Single(updateDialogService.Releases));
        Assert.Equal(version, Assert.Single(updateCheckService.SkippedVersions));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_OpensReleasePage_WhenUserChoosesOpen()
    {
        var release = CreateReleaseUpdateInfo("v1.0.2");
        var updateCheckService = new FakeUpdateCheckService();
        updateCheckService.Results.Enqueue(UpdateCheckResult.UpdateAvailable(release));
        var updateDialogService = new FakeUpdateDialogService
        {
            Result = UpdateDialogResult.OpenReleasePage
        };
        var externalLinkService = new FakeExternalLinkService();
        var viewModel = CreateViewModel(
            updateCheckService: updateCheckService,
            updateDialogService: updateDialogService,
            externalLinkService: externalLinkService);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(release.HtmlUrl, Assert.Single(externalLinkService.OpenedUris));
        Assert.Empty(updateCheckService.SkippedVersions);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShowsWarning_WhenOpeningReleasePageFails()
    {
        var release = CreateReleaseUpdateInfo("v1.0.2");
        var updateCheckService = new FakeUpdateCheckService();
        updateCheckService.Results.Enqueue(UpdateCheckResult.UpdateAvailable(release));
        var updateDialogService = new FakeUpdateDialogService
        {
            Result = UpdateDialogResult.OpenReleasePage
        };
        var externalLinkService = new FakeExternalLinkService
        {
            OpenException = new InvalidOperationException("browser unavailable")
        };
        var notificationService = new FakeNotificationService();
        var viewModel = CreateViewModel(
            notificationService: notificationService,
            updateCheckService: updateCheckService,
            updateDialogService: updateDialogService,
            externalLinkService: externalLinkService);

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        var warning = Assert.Single(notificationService.Warnings);
        Assert.Equal("打开 Release 页面失败", warning.Title);
        Assert.Contains("browser unavailable", warning.Message);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_DoesNotStartSecondRequest_WhenStartupCheckIsRunning()
    {
        var release = CreateReleaseUpdateInfo("v1.0.2");
        var startupCheckStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStartupCheckToComplete = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCheckService = new FakeUpdateCheckService
        {
            CheckHandler = async (mode, _) =>
            {
                startupCheckStarted.TrySetResult(null);
                await allowStartupCheckToComplete.Task;
                return UpdateCheckResult.UpdateAvailable(release);
            }
        };
        var updateDialogService = new FakeUpdateDialogService();
        var viewModel = CreateViewModel(
            updateCheckService: updateCheckService,
            updateDialogService: updateDialogService);

        await viewModel.InitializeAsync();
        await startupCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsCheckingForUpdates);
        Assert.Equal([UpdateCheckMode.Automatic], updateCheckService.CheckModes);

        allowStartupCheckToComplete.SetResult(null);
        await WaitForAsync(() => updateDialogService.Releases.Count == 1);

        Assert.Same(release, Assert.Single(updateDialogService.Releases));
        Assert.False(viewModel.IsCheckingForUpdates);
    }

    [Fact]
    public async Task ThemePreview_UpdatesImmediately_ThenAutoSavesSettings()
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
            themeService.ApplySettingsCalls >= 2 &&
            themeService.LastAppliedTheme?.Mode == AppThemeMode.Dark &&
            themeService.LastAppliedTheme?.UseSystemAccent == false);

        await WaitForAsync(() =>
            settingsService.SaveCalls > 0 &&
            settingsService.CurrentSettings.Ui.Theme?.Mode == AppThemeMode.Dark &&
            settingsService.CurrentSettings.Ui.Theme?.UseSystemAccent == false);
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
        Assert.True(viewModel.HomeReservationProgressValue > 0);

        await viewModel.CancelCurrentReservationCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasNoCurrentReservation);
        Assert.Null(viewModel.WorkflowState.CurrentReservation);
        Assert.Equal("当前未查询到预约", viewModel.RemoteCheckInPage.CurrentReservationText);
        Assert.Equal("--", viewModel.HomeReservationSeatNumberText);
        Assert.Equal(0, viewModel.HomeReservationProgressValue);
        Assert.Contains(notifications.Successes, x => x.Title == "已取消预约");
    }

    [Fact]
    public async Task CancelCurrentReservationAsync_IgnoresRefreshThatStartedBeforeCancellation()
    {
        var reservation = new ReservationInfo(
            "token-1",
            1,
            "自科阅览区一",
            "seat-4",
            "4",
            DateTimeOffset.Now.AddMinutes(30));
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var staleRefreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRefreshResult = new TaskCompletionSource<ReservationInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                refreshCalls++;
                if (refreshCalls == 1)
                {
                    return Task.FromResult<ReservationInfo?>(reservation);
                }

                staleRefreshStarted.TrySetResult(true);
                return staleRefreshResult.Task;
            },
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true)
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient);

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);
        var pendingRefresh = viewModel.RefreshReservationCommand.ExecuteAsync(null);
        await staleRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await viewModel.CancelCurrentReservationCommand.ExecuteAsync(null);
        staleRefreshResult.SetResult(reservation);
        await pendingRefresh;

        Assert.Null(viewModel.WorkflowState.CurrentReservation);
        Assert.True(viewModel.HasNoCurrentReservation);
        Assert.Equal("当前未查询到预约", viewModel.RemoteCheckInPage.CurrentReservationText);
    }

    [Fact]
    public async Task WorkflowStateCurrentReservationChange_UpdatesOccupyPresentation()
    {
        var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        var reservation = new ReservationInfo(
            "token-1",
            1,
            "自科阅览区一",
            "seat-4",
            "4",
            DateTimeOffset.Now.AddMinutes(30));

        viewModel.WorkflowState.CurrentReservation = reservation;

        await WaitForAsync(() => viewModel.Pages.OccupyPage.CurrentReservation == reservation);

        viewModel.WorkflowState.CurrentReservation = null;

        await WaitForAsync(() => viewModel.Pages.OccupyPage.CurrentReservation is null);
        Assert.True(viewModel.HasNoCurrentReservation);
        Assert.Equal("--", viewModel.HomeReservationSeatNumberText);
    }

    [Fact]
    public async Task RefreshReservationAsync_UsesFixedHomeProgressDuration_WhenConfigured()
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
                DateTimeOffset.Now.AddMinutes(5)))
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient);
        viewModel.SelectedHomeReservationProgressTimingModeIndex = 0;
        viewModel.HomeReservationFixedDurationMinutes = 10;

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);

        Assert.InRange(viewModel.HomeReservationProgressValue, 45, 55);
    }

    [Fact]
    public async Task RefreshReservationAsync_UsesSoftwareRuntimeHomeProgressDuration_WhenConfigured()
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
                DateTimeOffset.Now.AddMinutes(10)))
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient);
        viewModel.SelectedHomeReservationProgressTimingModeIndex = 1;

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);

        Assert.True(viewModel.HomeReservationProgressValue > 95);
    }

    [Fact]
    public async Task RefreshReservationAsync_KeepsSoftwareRuntimeHomeProgressStart_WhenReservationTokenChanges()
    {
        var observedAt = CreateLocalTimestamp(2026, 5, 5, 10, 0, 0);
        var expiresAt = observedAt.AddMinutes(10);
        var timeProvider = new FakeTimeProvider(observedAt);
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, observedAt, true)
        };
        var reservationInfoCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                reservationInfoCalls++;
                return Task.FromResult<ReservationInfo?>(new ReservationInfo(
                    $"volatile-token-{reservationInfoCalls}",
                    1,
                    "自科阅览区一",
                    "seat-4",
                    "4",
                    expiresAt));
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient,
            timeProvider: timeProvider);
        viewModel.SelectedHomeReservationProgressTimingModeIndex = 1;

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        await viewModel.RefreshReservationCommand.ExecuteAsync(null);

        Assert.Equal(2, reservationInfoCalls);
        Assert.InRange(viewModel.HomeReservationProgressValue, 49.9, 50.1);
    }

    [Theory]
    [InlineData(50, "#14804A")]
    [InlineData(20, "#C27803")]
    [InlineData(5, "#C93C37")]
    public async Task RefreshReservationAsync_TintsHomeProgressByRemainingPercent(
        int remainingMinutes,
        string expectedColor)
    {
        var sessionService = new FakeSessionService
        {
            CurrentSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(new ReservationInfo(
                $"token-{remainingMinutes}",
                1,
                "自科阅览区一",
                "seat-4",
                "4",
                DateTimeOffset.Now.AddMinutes(remainingMinutes)))
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            apiClient: apiClient);
        viewModel.SelectedHomeReservationProgressTimingModeIndex = 0;
        viewModel.HomeReservationFixedDurationMinutes = 100;

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);

        Assert.Equal(Color.Parse(expectedColor), GetBrushColor(viewModel.HomeReservationProgressBrush));
    }

    [Fact]
    public async Task ValidateManualCookieAsync_UsesFixedHomeCookieProgressDuration_WhenConfigured()
    {
        var observedAt = CreateLocalTimestamp(2026, 5, 5, 10, 0, 0);
        var expiresAt = observedAt.AddMinutes(60);
        var timeProvider = new FakeTimeProvider(observedAt);
        var viewModel = CreateViewModel(timeProvider: timeProvider);
        viewModel.SelectedHomeCookieProgressTimingModeIndex = 0;
        viewModel.HomeCookieFixedDurationMinutes = 120;
        viewModel.ManualCookieText = BuildAuthorizationCookie(expiresAt);

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasCurrentCookie);
        Assert.False(viewModel.HasNoCurrentCookie);
        Assert.Equal("有效中", viewModel.HomeCookieBadgeText);
        Assert.Contains("11:00:00", viewModel.HomeCookieExpirationTimeText);
        Assert.Equal("01:00:00", viewModel.HomeCookieRemainingText);
        Assert.InRange(viewModel.HomeCookieProgressValue, 49.9, 50.1);
        Assert.Equal(Color.Parse("#14804A"), GetBrushColor(viewModel.HomeCookieProgressBrush));
    }

    [Fact]
    public async Task ValidateManualCookieAsync_ShowsLoggedOutHomeCookieCard_WhenCookieExpired()
    {
        var observedAt = CreateLocalTimestamp(2026, 5, 5, 10, 0, 0);
        var timeProvider = new FakeTimeProvider(observedAt);
        var viewModel = CreateViewModel(timeProvider: timeProvider);
        viewModel.ManualCookieText = BuildAuthorizationCookie(observedAt.AddMinutes(-1));

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasNoCurrentCookie);
        Assert.Equal("未登录", viewModel.HomeCookieBadgeText);
        Assert.Equal("--:--:--", viewModel.HomeCookieExpirationTimeText);
        Assert.Equal("--", viewModel.HomeCookieRemainingText);
        Assert.Equal(0, viewModel.HomeCookieProgressValue);
    }

    [Fact]
    public async Task CanShowVenueConfiguration_TracksCookieValidity()
    {
        var observedAt = CreateLocalTimestamp(2026, 5, 5, 10, 0, 0);
        var timeProvider = new FakeTimeProvider(observedAt);
        var viewModel = CreateViewModel(timeProvider: timeProvider);

        Assert.False(viewModel.CanShowVenueConfiguration);
        Assert.True(viewModel.ShouldShowAuthorizationInput);

        viewModel.ManualCookieText = BuildAuthorizationCookie(observedAt.AddHours(1));
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthorized);
        Assert.True(viewModel.CanShowVenueConfiguration);
        Assert.False(viewModel.ShouldShowAuthorizationInput);

        timeProvider.Advance(TimeSpan.FromHours(2));
        viewModel.SelectedHomeCookieProgressTimingModeIndex = 1;

        Assert.True(viewModel.IsAuthorized);
        Assert.False(viewModel.CanShowVenueConfiguration);
        Assert.True(viewModel.ShouldShowAuthorizationInput);
    }

    [Fact]
    public async Task ValidateManualCookieAsync_KeepsSoftwareRuntimeHomeCookieProgressStart()
    {
        var observedAt = CreateLocalTimestamp(2026, 5, 5, 10, 0, 0);
        var expiresAt = observedAt.AddHours(1);
        var timeProvider = new FakeTimeProvider(observedAt);
        var viewModel = CreateViewModel(timeProvider: timeProvider);
        viewModel.SelectedHomeCookieProgressTimingModeIndex = 1;
        viewModel.ManualCookieText = BuildAuthorizationCookie(expiresAt);

        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        viewModel.HomeCookieFixedDurationMinutes = 121;

        Assert.InRange(viewModel.HomeCookieProgressValue, 49.9, 50.1);
    }

    [Fact]
    public async Task HomeCookieProgressSettings_AutoSavePreferences()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings());
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.SelectedHomeCookieProgressTimingModeIndex = 1;
        viewModel.HomeCookieFixedDurationMinutes = 150;

        await WaitForAsync(() =>
            settingsService.CurrentSettings.Ui.HomeCookieProgress?.Mode ==
            HomeCookieProgressTimingMode.SoftwareRuntimeDuration &&
            settingsService.CurrentSettings.Ui.HomeCookieProgress?.FixedDurationMinutes == 150);
    }

    [Fact]
    public async Task InitializeAsync_LoadsAutoReleaseSettings()
    {
        var viewModel = CreateViewModel(
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 120)));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.AutoReleaseReservationEnabled);
        Assert.Equal(120, viewModel.AutoReleaseLeadSeconds);
        Assert.Contains("120", viewModel.AutoReleaseStatusText);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsAutoReleaseSettings()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(settingsService: settingsService);
        await viewModel.InitializeAsync();

        viewModel.AutoReleaseReservationEnabled = true;
        viewModel.AutoReleaseLeadSeconds = 75;
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);

        Assert.True(settingsService.CurrentSettings.Tasks.AutoRelease.Enabled);
        Assert.Equal(75, settingsService.CurrentSettings.Tasks.AutoRelease.LeadSeconds);
    }

    [Fact]
    public async Task AutoRelease_CancelsCurrentReservation_WhenInsideLeadWindow()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
        };
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(cancelCalls == 0
                ? CreateReservation(
                    "token-auto",
                    DateTimeOffset.Now.AddSeconds(30))
                : null),
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(true);
            }
        };
        var notifications = new FakeNotificationService();
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: false, leadSeconds: 60)),
            apiClient: apiClient,
            notificationService: notifications);
        await viewModel.InitializeAsync();

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);
        viewModel.AutoReleaseReservationEnabled = true;

        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return cancelCalls == 1 && viewModel.HasNoCurrentReservation;
        });
        Assert.Contains(notifications.Successes, x => x.Title == "已自动退座");
    }

    [Fact]
    public async Task AutoRelease_RefreshesCurrentReservationAfterInitialization_WhenNoVenueIsBound()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
        };
        var libraryService = new FakeLibraryService();
        var reservationInfoCalls = 0;
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                reservationInfoCalls++;
                return Task.FromResult<ReservationInfo?>(CreateReservation(
                    "token-startup",
                    DateTimeOffset.Now.AddSeconds(30)));
            },
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(true);
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 60)),
            apiClient: apiClient);

        await viewModel.InitializeAsync();

        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return reservationInfoCalls >= 1 &&
                   cancelCalls == 1 &&
                   viewModel.HasNoCurrentReservation;
        });
        Assert.Equal(0, libraryService.BindLibraryCalls);
    }

    [Fact]
    public async Task AutoRelease_RefreshesCurrentReservationAfterManualAuthorization()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            AuthenticateFromCookieResult = session
        };
        var reservationInfoCalls = 0;
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) =>
            {
                reservationInfoCalls++;
                return Task.FromResult<ReservationInfo?>(CreateReservation(
                    "token-manual-auth",
                    DateTimeOffset.Now.AddSeconds(30)));
            },
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(true);
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 60)),
            apiClient: apiClient);
        await viewModel.InitializeAsync();

        viewModel.ManualCookieText = "cookie";
        await viewModel.ValidateManualCookieCommand.ExecuteAsync(null);

        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return reservationInfoCalls >= 1 &&
                   cancelCalls == 1 &&
                   viewModel.HasNoCurrentReservation;
        });
    }

    [Fact]
    public async Task AutoRelease_SuccessNotificationFailure_DoesNotRecordCancellationFailure()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
        };
        var activityLogService = new ActivityLogService();
        var notifications = new FakeNotificationService
        {
            ShowSuccessException = new InvalidOperationException("toast failed")
        };
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(CreateReservation(
                "token-toast",
                DateTimeOffset.Now.AddSeconds(30))),
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(true);
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 60)),
            apiClient: apiClient,
            activityLogService: activityLogService,
            notificationService: notifications);

        await viewModel.InitializeAsync();

        await WaitForAsync(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return cancelCalls == 1 && viewModel.HasNoCurrentReservation;
        });
        Assert.Contains(
            activityLogService.Entries,
            entry => entry.Kind == LogEntryKind.Success &&
                     entry.Category == "AutoRelease" &&
                     entry.Message.Contains("已自动退座", StringComparison.Ordinal));
        Assert.Contains(
            activityLogService.Entries,
            entry => entry.Kind == LogEntryKind.Warning &&
                     entry.Category == "AutoRelease" &&
                     entry.Message.Contains("自动退座成功通知失败", StringComparison.Ordinal));
        Assert.DoesNotContain(
            activityLogService.Entries,
            entry => entry.Kind == LogEntryKind.Warning &&
                     entry.Category == "AutoRelease" &&
                     entry.Message.StartsWith("自动退座失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutoRelease_DoesNotCancel_WhenOccupyIsStarting()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
        };
        var occupyCoordinator = new FakeOccupySeatCoordinator();
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(CreateReservation(
                "token-occupy",
                DateTimeOffset.Now.AddSeconds(30))),
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(true);
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 60)),
            apiClient: apiClient,
            occupySeatCoordinator: occupyCoordinator);
        occupyCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Starting,
            "占座",
            "占座启动中",
            DateTimeOffset.Now,
            DateTimeOffset.Now,
            Reason: CoordinatorStatusReason.Running));
        Dispatcher.UIThread.RunJobs();
        await viewModel.InitializeAsync();

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, cancelCalls);
        Assert.True(viewModel.HasCurrentReservation);
        Assert.True(viewModel.IsAutoReleaseSuppressedByOccupy);
        Assert.Contains("暂停", viewModel.AutoReleaseStatusText);
    }

    [Fact]
    public async Task AutoRelease_FailureKeepsReservation_AndWritesWarning()
    {
        var session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = session,
            RestoreResult = session
        };
        var activityLogService = new ActivityLogService();
        var cancelCalls = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetReservationInfoAsync = (_, _) => Task.FromResult<ReservationInfo?>(CreateReservation(
                "token-fail",
                DateTimeOffset.Now.AddSeconds(30))),
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCalls++;
                return Task.FromResult(false);
            }
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            settingsService: new FakeSettingsService(WithAutoRelease(enabled: true, leadSeconds: 60)),
            apiClient: apiClient,
            activityLogService: activityLogService);
        await viewModel.InitializeAsync();

        await viewModel.RefreshReservationCommand.ExecuteAsync(null);

        await WaitForAsync(() => cancelCalls == 1);
        Assert.True(viewModel.HasCurrentReservation);
        Assert.Contains(
            activityLogService.Entries,
            entry => entry.Kind == LogEntryKind.Warning &&
                     entry.Category == "AutoRelease" &&
                     entry.Message.Contains("自动退座失败", StringComparison.Ordinal));
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
    public async Task GlobalLeakLibraryPicker_DoesNotClearBoundLibraryUsedByGrab()
    {
        var notifications = new FakeNotificationService();
        var (viewModel, _) = await CreateBoundGrabViewModelAsync(notificationService: notifications);
        var boundLibrary = viewModel.AccountVenue.LockedLibrary;

        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        await viewModel.OpenGrabSeatSelectionOverlayCommand.ExecuteAsync(null);

        Assert.NotNull(boundLibrary);
        Assert.Equal(boundLibrary, viewModel.AccountVenue.LockedLibrary);
        Assert.Equal(boundLibrary, viewModel.SelectedLibrary);
        Assert.True(viewModel.IsGrabSeatSelectionOverlayOpen);
        Assert.DoesNotContain(notifications.Warnings, warning => warning.Title == "未绑定场馆");
    }

    [Fact]
    public async Task StartGrabAsync_UsesLockedLibrary_WhenSelectedLibraryDrifts()
    {
        var (viewModel, coordinator) = await CreateBoundGrabViewModelAsync();
        viewModel.VisibleSeats[0].IsSelected = true;
        viewModel.SelectedLibrary = new LibrarySummary(999001, "临时预览场馆", "1层", true, 10, 1, 0);

        await viewModel.StartGrabCommand.ExecuteAsync(null);

        var plan = Assert.IsType<GrabSeatPlan>(coordinator.LastPlan);
        Assert.Equal(117580, plan.LibraryId);
        Assert.Equal("自科阅览区一", plan.LibraryName);
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
    public async Task ProtocolTemplateChanges_AutoSaveOverrides_WhenCustomProtocolEnabled()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings() with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var protocolTemplateStore = new FakeProtocolTemplateStore(TestProtocolTemplates.Create());
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            protocolTemplateStore: protocolTemplateStore);
        await viewModel.InitializeAsync();

        viewModel.TomorrowReservationSaveTemplateText = "save-override";

        await WaitForAsync(() =>
            protocolTemplateStore.SaveCalls > 0 &&
            protocolTemplateStore.LastOverrides?.TomorrowReservationSaveTemplate == "save-override");
    }

    [Fact]
    public async Task SaveProtocolOverridesAsync_IncludesTomorrowReservationTemplates()
    {
        var protocolTemplateStore = new FakeProtocolTemplateStore(TestProtocolTemplates.Create());
        var viewModel = CreateViewModel(protocolTemplateStore: protocolTemplateStore);
        await viewModel.InitializeAsync();
        viewModel.TomorrowReservationQueueUrlTemplateText = "wss://override.example/ws";
        viewModel.GraphQlEndpointUrlText = "https://override.example/graphql";
        viewModel.RemoteCheckInSignEndpointUrlText = "https://override.example/sign";
        viewModel.TomorrowReservationWarmUpTemplateText = "warm-override";
        viewModel.TomorrowReservationSaveTemplateText = "save-override";
        viewModel.TomorrowReservationInfoTemplateText = "info-override";

        await viewModel.SaveProtocolOverridesCommand.ExecuteAsync(null);

        Assert.Equal(1, protocolTemplateStore.SaveCalls);
        Assert.NotNull(protocolTemplateStore.LastOverrides);
        Assert.Equal("wss://override.example/ws", protocolTemplateStore.LastOverrides.TomorrowReservationQueueUrlTemplate);
        Assert.Equal("https://override.example/graphql", protocolTemplateStore.LastOverrides.GraphQlEndpointUrl);
        Assert.Equal("https://override.example/sign", protocolTemplateStore.LastOverrides.RemoteCheckInSignEndpointUrl);
        Assert.Equal("warm-override", protocolTemplateStore.LastOverrides.TomorrowReservationWarmUpTemplate);
        Assert.Equal("save-override", protocolTemplateStore.LastOverrides.TomorrowReservationSaveTemplate);
        Assert.Equal("info-override", protocolTemplateStore.LastOverrides.TomorrowReservationInfoTemplate);
        Assert.Null(protocolTemplateStore.LastOverrides.GetCookieUrlTemplate);
        Assert.Null(protocolTemplateStore.LastOverrides.GraphQlDefaultRefererUrl);
        Assert.Null(protocolTemplateStore.LastOverrides.RemoteCheckInDevicesEndpointUrl);
        Assert.Null(protocolTemplateStore.LastOverrides.QueryLibrariesTemplate);
        Assert.Null(protocolTemplateStore.LastOverrides.ReserveSeatTemplate);
    }

    [Fact]
    public async Task ProtocolAddressChanges_BlockInvalidAutoSave_AndResumeAfterCorrection()
    {
        var settingsService = new FakeSettingsService(CreateDesktopDefaultSettings() with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var protocolTemplateStore = new FakeProtocolTemplateStore(TestProtocolTemplates.Create());
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            protocolTemplateStore: protocolTemplateStore);
        await viewModel.InitializeAsync();

        viewModel.GraphQlEndpointUrlText = "relative/graphql";
        await Task.Delay(650);

        Assert.True(viewModel.HasProtocolValidationErrors);
        Assert.False(viewModel.SaveProtocolOverridesCommand.CanExecute(null));
        Assert.NotEmpty(viewModel.GetErrors(nameof(viewModel.GraphQlEndpointUrlText)).Cast<string>());
        Assert.Equal(0, protocolTemplateStore.SaveCalls);

        viewModel.GraphQlEndpointUrlText = "http://127.0.0.1:18080/graphql";
        await WaitForAsync(() => protocolTemplateStore.SaveCalls > 0);

        Assert.False(viewModel.HasProtocolValidationErrors);
        Assert.True(viewModel.SaveProtocolOverridesCommand.CanExecute(null));
        Assert.Equal("http://127.0.0.1:18080/graphql", protocolTemplateStore.LastOverrides?.GraphQlEndpointUrl);
        Assert.Null(protocolTemplateStore.LastOverrides?.GetCookieUrlTemplate);
        Assert.Null(protocolTemplateStore.LastOverrides?.QueryLibrariesTemplate);
    }

    [Fact]
    public void LaunchOnStartupEnabled_OnToggleToTrue_CallsEnableAsync()
    {
        var fakeStartup = new FakeStartupEntryService();
        var viewModel = CreateViewModel(startupEntryService: fakeStartup);
        viewModel.IsInitializationComplete = true;

        viewModel.LaunchOnStartupEnabled = true;

        Assert.True(fakeStartup.EnableCalled);
        Assert.False(fakeStartup.DisableCalled);
    }

    [Fact]
    public void LaunchOnStartupEnabled_OnToggleToFalse_CallsDisableAsync()
    {
        var fakeStartup = new FakeStartupEntryService();
        var viewModel = CreateViewModel(startupEntryService: fakeStartup);
        viewModel.IsInitializationComplete = true;

        viewModel.LaunchOnStartupEnabled = true;
        fakeStartup.Reset();
        viewModel.LaunchOnStartupEnabled = false;

        Assert.True(fakeStartup.DisableCalled);
        Assert.False(fakeStartup.EnableCalled);
    }

    [Fact]
    public void LaunchOnStartupEnabled_WhenNotInitialized_DoesNotCallStartupService()
    {
        var fakeStartup = new FakeStartupEntryService();
        var viewModel = CreateViewModel(startupEntryService: fakeStartup);

        // IsInitializationComplete is false by default
        viewModel.LaunchOnStartupEnabled = true;

        Assert.False(fakeStartup.EnableCalled);
    }

    [Fact]
    public async Task LaunchOnStartupEnabled_OnEnableFailure_RollsBackAndNotifies()
    {
        var fakeStartup = new FakeStartupEntryService();
        fakeStartup.EnableException = new InvalidOperationException("reg.exe not found");
        var fakeNotification = new FakeNotificationService();
        var viewModel = CreateViewModel(
            startupEntryService: fakeStartup,
            notificationService: fakeNotification);
        viewModel.IsInitializationComplete = true;

        // Fire-and-forget handler runs synchronously with fake services
        viewModel.LaunchOnStartupEnabled = true;

        // Allow any pending continuation to flush
        await Task.Yield();

        Assert.True(fakeStartup.EnableCalled);
        Assert.False(viewModel.LaunchOnStartupEnabled);
        Assert.NotEmpty(fakeNotification.Warnings);
    }

    [Fact]
    public async Task LaunchOnStartupEnabled_OnDisableFailure_RollsBackAndNotifies()
    {
        var fakeStartup = new FakeStartupEntryService();
        var fakeNotification = new FakeNotificationService();
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            startupEntryService: fakeStartup,
            notificationService: fakeNotification);
        viewModel.IsInitializationComplete = true;
        viewModel.LaunchOnStartupEnabled = true;
        fakeStartup.Reset();
        fakeStartup.DisableException = new InvalidOperationException("delete failed");

        viewModel.LaunchOnStartupEnabled = false;
        await Task.Yield();

        Assert.True(fakeStartup.DisableCalled);
        Assert.True(viewModel.LaunchOnStartupEnabled);
        Assert.Contains(fakeNotification.Warnings, warning => warning.Message.Contains("开启状态", StringComparison.Ordinal));
        await WaitForAsync(() => settingsService.CurrentSettings.Ui.LaunchOnStartup);
    }

    [Fact]
    public async Task LaunchOnStartupEnabled_WhenUnsupported_RollsBackAndPersistsFalse()
    {
        var fakeStartup = new FakeStartupEntryService { IsSupported = false };
        var fakeNotification = new FakeNotificationService();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                LaunchOnStartup = true
            }
        });
        var viewModel = CreateViewModel(
            settingsService: settingsService,
            startupEntryService: fakeStartup,
            notificationService: fakeNotification);
        viewModel.IsInitializationComplete = true;

        viewModel.LaunchOnStartupEnabled = true;

        Assert.False(viewModel.LaunchOnStartupEnabled);
        Assert.False(fakeStartup.EnableCalled);
        Assert.False(fakeStartup.DisableCalled);
        Assert.Contains(fakeNotification.Warnings, warning => warning.Title == "开机启动项不可用");
        await WaitForAsync(() => settingsService.CurrentSettings.Ui.LaunchOnStartup == false);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeSessionService? sessionService = null,
        FakeLibraryService? libraryService = null,
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeGrabSeatCoordinator? grabSeatCoordinator = null,
        FakeGlobalLeakCoordinator? globalLeakCoordinator = null,
        FakeOccupySeatCoordinator? occupySeatCoordinator = null,
        FakeTomorrowReservationCoordinator? tomorrowReservationCoordinator = null,
        FakeNotificationService? notificationService = null,
        FakeTaskEventAlertDispatcher? taskAlertService = null,
        FakeErrorDialogService? errorDialogService = null,
        FakeUpdateCheckService? updateCheckService = null,
        FakeUpdateDialogService? updateDialogService = null,
        FakeExternalLinkService? externalLinkService = null,
        FakeAppThemeService? appThemeService = null,
        ActivityLogService? activityLogService = null,
        FakeProtocolTemplateStore? protocolTemplateStore = null,
        FakeStartupEntryService? startupEntryService = null,
        FakeLanCookieRelayService? lanCookieRelayService = null,
        FakeQrCodeImageFactory? qrCodeImageFactory = null,
        FakeMobileControlService? mobileControlService = null,
        FakeTimeProvider? timeProvider = null,
        FakeNetworkExposureManager? networkExposureManager = null)
    {
        sessionService ??= new FakeSessionService();
        libraryService ??= new FakeLibraryService();
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        apiClient ??= new FakeTraceIntApiClient();
        grabSeatCoordinator ??= new FakeGrabSeatCoordinator();
        globalLeakCoordinator ??= new FakeGlobalLeakCoordinator();
        occupySeatCoordinator ??= new FakeOccupySeatCoordinator();
        tomorrowReservationCoordinator ??= new FakeTomorrowReservationCoordinator();
        taskAlertService ??= new FakeTaskEventAlertDispatcher();
        activityLogService ??= new ActivityLogService();

        return MainWindowViewModelTestHarness.Create(
            new SessionWorkflowService(apiClient, sessionService),
            new VenueWorkflowService(libraryService, new FakeSeatLabelService(), sessionService, apiClient, settingsService),
            new ReservationWorkflowService(sessionService, apiClient, occupySeatCoordinator, activityLogService),
            new SettingsWorkflowService(settingsService),
            new ProtocolTemplateEditorService(protocolTemplateStore ?? new FakeProtocolTemplateStore(TestProtocolTemplates.Create())),
            taskAlertService,
            grabSeatCoordinator,
            globalLeakCoordinator,
            occupySeatCoordinator,
            tomorrowReservationCoordinator,
            activityLogService,
            notificationService ?? new FakeNotificationService(),
            errorDialogService ?? new FakeErrorDialogService(),
            updateCheckService ?? new FakeUpdateCheckService(),
            updateDialogService ?? new FakeUpdateDialogService(),
            externalLinkService ?? new FakeExternalLinkService(),
            new FakeAppVersionProvider(),
            appThemeService ?? new FakeAppThemeService(),
            timeProvider ?? new FakeTimeProvider(),
            new AppWindowService(),
            startupEntryService ?? new FakeStartupEntryService(),
            lanCookieRelayService ?? new FakeLanCookieRelayService(),
            qrCodeImageFactory ?? new FakeQrCodeImageFactory(),
            mobileControlService,
            networkExposureManager: networkExposureManager);
    }

    private static ReleaseUpdateInfo CreateReleaseUpdateInfo(string tagName)
    {
        Assert.True(ReleaseVersion.TryParse(tagName, out var version));
        return new ReleaseUpdateInfo(
            version,
            tagName,
            $"IGoLibrary-Ex {tagName}",
            "更新内容",
            new Uri($"https://github.com/EJianZQ/IGoLibrary/releases/tag/{tagName}"),
            new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
            version.IsPrerelease);
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

    private static MainWindowViewModel CreateGlobalLeakViewModel(
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeGlobalLeakCoordinator? globalLeakCoordinator = null,
        FakeNotificationService? notificationService = null,
        ActivityLogService? activityLogService = null)
    {
        var libraries = new[]
        {
            new LibrarySummary(1, "场馆A", "3层", true, 120, 20, 10),
            new LibrarySummary(2, "场馆B", "5层", true, 80, 10, 5),
            new LibrarySummary(3, "场馆C", "7层", false, 60, 30, 10)
        };
        var globalLeakSession = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true);
        var sessionService = new FakeSessionService
        {
            CurrentSession = globalLeakSession,
            RestoreResult = globalLeakSession
        };
        var libraryService = new FakeLibraryService
        {
            LibrariesToLoad = libraries
        };
        var viewModel = CreateViewModel(
            sessionService: sessionService,
            libraryService: libraryService,
            settingsService: settingsService,
            apiClient: apiClient,
            globalLeakCoordinator: globalLeakCoordinator,
            notificationService: notificationService,
            activityLogService: activityLogService);
        viewModel.IsAuthorized = true;
        return viewModel;
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

    private static AppSettings WithGlobalLeakSelectedLibraries(
        params GlobalLeakLibrarySelectionSettings[] selectedLibraries)
        => AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                GlobalLeak = new GlobalLeakTaskSettings(selectedLibraries)
            }
        };

    private static AppSettings WithAutoRelease(bool enabled, int leadSeconds)
        => AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                AutoRelease = new AutoReleaseTaskSettings(enabled, leadSeconds)
            }
        };

    private static AppSettings WithTaskEventAlerts(TaskEventAlertSettings alerts)
        => AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = alerts
            }
        };

    private static ReservationInfo CreateReservation(string token, DateTimeOffset expirationTime)
    {
        return new ReservationInfo(
            token,
            1,
            "自科阅览区一",
            "seat-1",
            "1",
            expirationTime);
    }

    private static DateTimeOffset CreateLocalTimestamp(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second)
    {
        var localTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
    }

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
