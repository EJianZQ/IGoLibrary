using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class HealthCheckServiceTests
{
    [Fact]
    public async Task RunPreflightAsync_BlocksGrab_WhenCookieVenueSeatsOrTemplatesAreMissing()
    {
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("Authorization=token", SessionSource.ManualCookie, DateTimeOffset.Now, true)
        };
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var apiClient = new FakeTraceIntApiClient
        {
            OnValidateCookieAsync = (_, _) => Task.FromException(
                new InvalidOperationException("登录已失效，请重新授权"))
        };
        var service = CreateService(
            runtimeState,
            settingsService,
            apiClient,
            new FakeProtocolTemplateStore(new TraceIntGraphQlTemplates(
                "cookie-url",
                "",
                "layout",
                "rule",
                "reservation",
                "",
                "cancel")));

        var result = await service.RunPreflightAsync(PreflightTarget.Grab([]));

        Assert.False(result.CanStart);
        Assert.Contains(result.Items, item =>
            item.Key == "cookie" &&
            item.Severity == HealthSeverity.Blocking &&
            item.Message.Contains("失效", StringComparison.Ordinal));
        Assert.Contains(result.Items, item =>
            item.Key == "venue" &&
            item.Severity == HealthSeverity.Blocking);
        Assert.Contains(result.Items, item =>
            item.Key == "seat-selection" &&
            item.Severity == HealthSeverity.Blocking);
        Assert.Contains(result.Items, item =>
            item.Key == "protocol-template" &&
            item.Severity == HealthSeverity.Blocking);
    }

    [Fact]
    public async Task RunPreflightAsync_WarnsButAllowsStart_WhenOnlyNotificationChannelsAreMissing()
    {
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true),
            BoundLibrary = new LibrarySummary(1, "自科", "3层", true)
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Notifications = new NotificationSettings(
                appBannerNotificationsEnabled: false,
                taskEventAlerts: new TaskEventAlertSettings(
                    EmailAlertChannelSettings.Default,
                    new LocalDesktopAlertSettings(false, false),
                    TelegramAlertChannelSettings.Default,
                    BarkAlertChannelSettings.Default))
        });
        var service = CreateService(runtimeState, settingsService);

        var result = await service.RunPreflightAsync(PreflightTarget.Grab(
            [new SeatReference("seat-1", "1号座")]));

        Assert.True(result.CanStart);
        Assert.Contains(result.Items, item =>
            item.Key == "notification" &&
            item.Severity == HealthSeverity.Warning);
    }

    [Fact]
    public async Task BuildSnapshotAsync_IncludesTaskStatesAndCurrentReservation()
    {
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true),
            BoundLibrary = new LibrarySummary(1, "自科", "3层", true),
            CurrentReservation = new ReservationInfo(
                "reserve-token",
                1,
                "自科",
                "seat-1",
                "1号座",
                DateTimeOffset.Now.AddMinutes(20))
        };
        var grabCoordinator = new FakeGrabSeatCoordinator();
        grabCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "抢座运行中",
            DateTimeOffset.Now,
            DateTimeOffset.Now));
        var service = CreateService(runtimeState, grabSeatCoordinator: grabCoordinator);

        var snapshot = await service.BuildSnapshotAsync();

        Assert.Contains(snapshot.Items, item =>
            item.Key == "reservation" &&
            item.Message.Contains("1号座", StringComparison.Ordinal));
        Assert.Contains(snapshot.Items, item =>
            item.Key == "task.grab" &&
            item.Message.Contains("抢座运行中", StringComparison.Ordinal));
    }

    private static IHealthCheckService CreateService(
        AppRuntimeState runtimeState,
        FakeSettingsService? settingsService = null,
        FakeTraceIntApiClient? apiClient = null,
        FakeProtocolTemplateStore? protocolTemplateStore = null,
        FakeGrabSeatCoordinator? grabSeatCoordinator = null)
    {
        settingsService ??= new FakeSettingsService(AppSettings.Default);
        apiClient ??= new FakeTraceIntApiClient();
        protocolTemplateStore ??= new FakeProtocolTemplateStore(new TraceIntGraphQlTemplates(
            "cookie-url",
            "libraries",
            "layout",
            "rule",
            "reservation",
            "reserve",
            "cancel",
            "queue",
            "warm",
            "save",
            "tomorrow-info"));

        return new HealthCheckService(
            runtimeState,
            runtimeState,
            runtimeState,
            settingsService,
            apiClient,
            protocolTemplateStore,
            grabSeatCoordinator ?? new FakeGrabSeatCoordinator(),
            new FakeVenueAvailabilityCoordinator(),
            new FakeOccupySeatCoordinator(),
            new FakeTomorrowReservationCoordinator(),
            new FakeCheckInGuardCoordinator());
    }
}
