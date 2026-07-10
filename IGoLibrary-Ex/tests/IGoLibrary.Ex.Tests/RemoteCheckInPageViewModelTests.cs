using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class RemoteCheckInPageViewModelTests
{
    private const string BeaconUuid = "E2C56DB5-DFFB-48D2-B060-D0F5A71096E0";
    private const string OtherBeaconUuid = "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE";

    [Fact]
    public async Task Authorization_UsesFreshCodeAndMasksStudentNumber()
    {
        var (viewModel, state, workflow, _, _) = CreateViewModel();
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();
        viewModel.AuthorizationLinkText = $"https://example.test/?code={new string('b', 32)}";

        await viewModel.AuthorizeFromLinkCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRemoteCheckInSession);
        Assert.Equal(BeaconUuid, viewModel.SelectedBeaconUuid);
        Assert.Contains("20****01", viewModel.AccountSummaryText);
        Assert.DoesNotContain(workflow.CurrentSession!.Token, viewModel.AuthorizationStatusText);
    }

    [Fact]
    public async Task Authorization_DisplaysCookieExpirationInLocalTime()
    {
        var (viewModel, state, workflow, _, _) = CreateViewModel();
        var expiresAt = new DateTimeOffset(2026, 7, 10, 2, 33, 4, TimeSpan.Zero);
        workflow.OnAuthorizeAsync = (_, remember, _) =>
        {
            var session = new RemoteCheckInSessionCredentials(
                new string('a', 40),
                DateTimeOffset.UtcNow,
                remember,
                expiresAt);
            return Task.FromResult(new RemoteCheckInAuthorizationResult(
                session,
                new RemoteCheckInDeviceInfo(
                    new RemoteCheckInUserSummary("测试用户", "测试学校", "测试学生", "20240001"),
                    [BeaconUuid]),
                null));
        };
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();
        viewModel.AuthorizationLinkText = $"https://example.test/?code={new string('b', 32)}";

        await viewModel.AuthorizeFromLinkCommand.ExecuteAsync(null);

        Assert.Equal(
            $"签到授权到期时间：{expiresAt.ToLocalTime():M月d日 HH:mm:ss}",
            viewModel.AuthorizationExpirationText);
    }

    [Fact]
    public async Task ExpirationCheck_ClearsPresentationAndRequestsReauthorization()
    {
        var (viewModel, state, workflow, _, notifications) = CreateViewModel();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(
            new string('a', 40),
            expiresAt.AddHours(-1),
            true,
            expiresAt);
        workflow.OnClearExpiredSessionAsync = _ => Task.FromResult(true);
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();

        viewModel.QueueAuthorizationExpirationCheck(expiresAt.AddSeconds(1));

        Assert.False(viewModel.HasRemoteCheckInSession);
        Assert.Equal("签到授权到期时间：已到期", viewModel.AuthorizationExpirationText);
        Assert.Contains("已到期", viewModel.AuthorizationStatusText);
        Assert.Contains(notifications.Warnings, item => item.Title == "签到授权已到期");
    }

    [Fact]
    public async Task Reauthorization_WhenCandidateIsInvalid_RetainsPreviousSessionAndReportsFailure()
    {
        var (viewModel, state, workflow, _, notifications) = CreateViewModel();
        var previous = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        workflow.CurrentSession = previous;
        workflow.OnAuthorizeAsync = (_, _, _) => throw new RemoteCheckInAuthorizationException(
            "签到接口错误：未登录",
            isSessionInvalid: true);
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();
        viewModel.AuthorizationLinkText = $"https://example.test/?code={new string('b', 32)}";

        await viewModel.AuthorizeFromLinkCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRemoteCheckInSession);
        Assert.Same(previous, workflow.CurrentSession);
        Assert.Contains("已保留原签到授权", viewModel.AuthorizationStatusText);
        Assert.Contains(notifications.Warnings, item => item.Title == "获取签到授权失败");

        await viewModel.AuthorizeFromLinkCommand.ExecuteAsync(null);
        Assert.Contains(notifications.Warnings, item => item.Title == "链接已使用");
    }

    [Fact]
    public async Task ClearAuthorization_WhenCredentialDeleteFails_KeepsSessionAndAllowsRetry()
    {
        var (viewModel, state, workflow, _, notifications) = CreateViewModel();
        var previous = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        workflow.CurrentSession = previous;
        workflow.ClearException = new InvalidOperationException("credential delete failed");
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();

        await viewModel.ClearAuthorizationCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasRemoteCheckInSession);
        Assert.Same(previous, workflow.CurrentSession);
        Assert.Contains("原授权仍保留", viewModel.AuthorizationStatusText);
        Assert.Contains(notifications.Warnings, item => item.Title == "清除签到授权失败");
    }

    [Fact]
    public async Task MultipleDevices_RequireExplicitSelection()
    {
        var (viewModel, state, workflow, _, _) = CreateViewModel();
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        workflow.OnGetDeviceInfoAsync = _ => Task.FromResult(new RemoteCheckInDeviceInfo(
            new RemoteCheckInUserSummary("", "", "", ""),
            [BeaconUuid, OtherBeaconUuid]));
        await viewModel.InitializeAsync();

        await viewModel.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedBeaconUuid);
        Assert.False(viewModel.CanSaveProfile);
    }

    [Fact]
    public async Task MultipleDevices_WhenSelectionChanges_ClearsBeaconIdsButKeepsCoordinates()
    {
        var (viewModel, state, workflow, _, _) = CreateViewModel();
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        workflow.OnGetDeviceInfoAsync = _ => Task.FromResult(new RemoteCheckInDeviceInfo(
            new RemoteCheckInUserSummary("", "", "", ""),
            [BeaconUuid, OtherBeaconUuid]));
        await viewModel.InitializeAsync();
        await viewModel.RefreshDevicesCommand.ExecuteAsync(null);
        viewModel.SelectedBeaconUuid = BeaconUuid;
        viewModel.BeaconMajor = 100;
        viewModel.BeaconMinor = 200;
        viewModel.Latitude = 39.1m;
        viewModel.Longitude = 116.2m;

        viewModel.SelectedBeaconUuid = OtherBeaconUuid;

        Assert.Null(viewModel.BeaconMajor);
        Assert.Null(viewModel.BeaconMinor);
        Assert.Equal(39.1m, viewModel.Latitude);
        Assert.Equal(116.2m, viewModel.Longitude);
        Assert.False(viewModel.IsProfileSaved);
    }

    [Fact]
    public async Task LockedLibrarySwitch_IgnoresProfileThatCompletesForPreviousLibrary()
    {
        var (viewModel, state, _, profiles, _) = CreateViewModel();
        var libraryA = CreateLibrary();
        var libraryB = new LibrarySummary(2, "测试馆B", "2楼", true);
        var profileA = CreateProfile();
        var profileB = new RemoteCheckInVenueProfileSettings
        {
            LibraryId = libraryB.LibraryId,
            LibraryName = libraryB.Name,
            BeaconUuid = OtherBeaconUuid,
            Major = 22,
            Minor = 33,
            Latitude = 31.2m,
            Longitude = 121.5m
        };
        var pendingA = new TaskCompletionSource<RemoteCheckInVenueProfileSettings?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingB = new TaskCompletionSource<RemoteCheckInVenueProfileSettings?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        profiles.OnGetForLibraryAsync = (libraryId, _) =>
            libraryId == libraryA.LibraryId ? pendingA.Task : pendingB.Task;

        state.LockedLibrary = libraryA;
        var explicitLoadA = viewModel.LoadProfileForLockedLibraryAsync();
        state.LockedLibrary = libraryB;
        var explicitLoadB = viewModel.LoadProfileForLockedLibraryAsync();

        pendingB.SetResult(profileB);
        await explicitLoadB;
        Assert.Equal(OtherBeaconUuid, viewModel.SelectedBeaconUuid);
        Assert.Equal(22, viewModel.BeaconMajor);

        pendingA.SetResult(profileA);
        await explicitLoadA;

        Assert.Equal(libraryB.LibraryId, state.LockedLibrary?.LibraryId);
        Assert.Equal(OtherBeaconUuid, viewModel.SelectedBeaconUuid);
        Assert.Equal(22, viewModel.BeaconMajor);
        Assert.Contains(libraryB.Name, viewModel.ProfileStatusText);
    }

    [Fact]
    public async Task LockedLibrarySwitch_BackToLoadedLibrary_InvalidatesPendingOtherLibrary()
    {
        var (viewModel, state, _, profiles, _) = CreateViewModel();
        var libraryA = CreateLibrary();
        var libraryB = new LibrarySummary(2, "测试馆B", "2楼", true);
        var profileB = new RemoteCheckInVenueProfileSettings
        {
            LibraryId = libraryB.LibraryId,
            LibraryName = libraryB.Name,
            BeaconUuid = OtherBeaconUuid,
            Major = 22,
            Minor = 33,
            Latitude = 31.2m,
            Longitude = 121.5m
        };
        profiles.Profiles[libraryB.LibraryId] = profileB;
        state.LockedLibrary = libraryB;
        await viewModel.LoadProfileForLockedLibraryAsync();

        var pendingA = new TaskCompletionSource<RemoteCheckInVenueProfileSettings?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        profiles.OnGetForLibraryAsync = (libraryId, _) =>
            libraryId == libraryA.LibraryId
                ? pendingA.Task
                : Task.FromResult<RemoteCheckInVenueProfileSettings?>(profileB);
        state.LockedLibrary = libraryA;
        var explicitLoadA = viewModel.LoadProfileForLockedLibraryAsync();
        state.LockedLibrary = libraryB;
        await viewModel.LoadProfileForLockedLibraryAsync();

        pendingA.SetResult(CreateProfile());
        await explicitLoadA;

        Assert.Equal(libraryB.LibraryId, state.LockedLibrary?.LibraryId);
        Assert.Equal(OtherBeaconUuid, viewModel.SelectedBeaconUuid);
        Assert.Equal(22, viewModel.BeaconMajor);
        Assert.Contains(libraryB.Name, viewModel.ProfileStatusText);
    }

    [Fact]
    public async Task RefreshDevices_AfterEditingSavedDraft_KeepsDraftUnsaved()
    {
        var profile = CreateProfile();
        var (viewModel, state, workflow, profiles, _) = CreateViewModel();
        profiles.Profiles[1] = profile;
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();
        viewModel.BeaconMajor = 999;

        await viewModel.RefreshDevicesCommand.ExecuteAsync(null);

        Assert.Equal(999, viewModel.BeaconMajor);
        Assert.False(viewModel.IsProfileSaved);
        Assert.False(viewModel.CanSign);
        Assert.Contains("重新保存", viewModel.ProfileStatusText);
    }

    [Fact]
    public async Task Sign_BlocksReservationFromDifferentLibrary()
    {
        var profile = CreateProfile();
        var (viewModel, state, workflow, profiles, notifications) = CreateViewModel(
            new ReservationOperationResult(true, new ReservationInfo("t", 99, "其他馆", "1,1", "001", DateTimeOffset.Now.AddMinutes(30))));
        profiles.Profiles[1] = profile;
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();

        await viewModel.SignCommand.ExecuteAsync(null);

        Assert.Contains(notifications.Warnings, item => item.Message.Contains("当前预约与锁定场馆不一致", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sign_SucceedsAndRefreshesReservationState()
    {
        var reservation = new ReservationInfo("t", 1, "测试馆", "1,1", "001", DateTimeOffset.Now.AddMinutes(30));
        var profile = CreateProfile();
        var (viewModel, state, workflow, profiles, notifications) = CreateViewModel(
            new ReservationOperationResult(true, reservation));
        profiles.Profiles[1] = profile;
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();

        await viewModel.SignCommand.ExecuteAsync(null);

        Assert.Contains(notifications.Successes, item => item.Title == "远程签到成功");
        Assert.Same(reservation, state.CurrentReservation);
        Assert.Contains("验证成功", viewModel.LastResultText);
    }

    [Fact]
    public async Task Sign_WhenOutcomeIsUnknown_ShowsWarningAndDoesNotRetry()
    {
        var reservation = new ReservationInfo("t", 1, "测试馆", "1,1", "001", DateTimeOffset.Now.AddMinutes(30));
        var (viewModel, state, workflow, profiles, notifications) = CreateViewModel(
            new ReservationOperationResult(true, reservation));
        profiles.Profiles[1] = CreateProfile();
        workflow.CurrentSession = new RemoteCheckInSessionCredentials(new string('a', 40), DateTimeOffset.UtcNow, true);
        var signCalls = 0;
        workflow.OnSignAsync = (_, _) =>
        {
            signCalls++;
            throw new RemoteCheckInOutcomeUnknownException(
                "签到请求的结果未知，请先核对预约状态。",
                new TimeoutException());
        };
        state.IsAuthorized = true;
        state.LockedLibrary = CreateLibrary();
        await viewModel.InitializeAsync();

        await viewModel.SignCommand.ExecuteAsync(null);

        Assert.Equal(1, signCalls);
        Assert.Contains("结果未知", viewModel.LastResultText);
        Assert.Contains(notifications.Warnings, item => item.Title == "签到结果未知");
        Assert.DoesNotContain(notifications.Successes, item => item.Title == "远程签到成功");
    }

    [Fact]
    public void OAuthCodeRegistry_PreventsCrossFlowReuseAfterConsumption()
    {
        var registry = new OAuthCodeConsumptionRegistry();
        var code = new string('a', 32);

        Assert.True(registry.TryReserve(code));
        registry.Complete(code, markAsProcessed: true);
        Assert.False(registry.TryReserve(code));
    }

    private static (
        RemoteCheckInPageViewModel ViewModel,
        ShellWorkflowState State,
        FakeRemoteCheckInWorkflowService Workflow,
        FakeRemoteCheckInProfileService Profiles,
        FakeNotificationService Notifications) CreateViewModel(ReservationOperationResult? reservation = null)
    {
        var state = new ShellWorkflowState();
        var workflow = new FakeRemoteCheckInWorkflowService();
        var profiles = new FakeRemoteCheckInProfileService();
        var notifications = new FakeNotificationService();
        var relay = new LanCookieRelayViewModel(
            new FakeLanCookieRelayService(),
            new FakeQrCodeImageFactory(),
            new ActivityLogService(),
            notifications);
        var reservationWorkflow = new FakeReservationWorkflowService
        {
            OnRefreshAsync = _ => Task.FromResult(reservation ?? new ReservationOperationResult(true, null))
        };
        var viewModel = new RemoteCheckInPageViewModel(
            workflow,
            profiles,
            reservationWorkflow,
            state,
            new OAuthCodeConsumptionRegistry(),
            relay,
            new ActivityLogService(),
            notifications);
        return (viewModel, state, workflow, profiles, notifications);
    }

    private static LibrarySummary CreateLibrary() => new(1, "测试馆", "1楼", true);

    private static RemoteCheckInVenueProfileSettings CreateProfile() => new()
    {
        LibraryId = 1,
        LibraryName = "测试馆",
        BeaconUuid = BeaconUuid,
        Major = 1,
        Minor = 2,
        Latitude = 39.1m,
        Longitude = 116.2m
    };
}
