using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlStatusSnapshotProviderTests
{
    [Fact]
    public async Task CreateSnapshotAsync_MapsSessionReservationTasksAndLogs()
    {
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var runtimeState = new AppRuntimeState
        {
            Session = new SessionCredentials(
                "Authorization=secret-token; SERVERID=s",
                SessionSource.QrCodeLink,
                now.AddMinutes(-10),
                true)
        };
        var workflowState = new ShellWorkflowState
        {
            CurrentCookieExpirationTime = now.AddHours(1),
            CurrentReservation = new ReservationInfo(
                "reservation-token",
                1,
                "自科",
                "seat-1",
                "1",
                now.AddMinutes(2))
        };
        runtimeState.CurrentReservation = workflowState.CurrentReservation;

        var grabCoordinator = new FakeGrabSeatCoordinator();
        grabCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "抢座运行中。",
            now.AddSeconds(-30),
            now.AddSeconds(-1),
            PollCount: 5,
            RequestCount: 3,
            LastRequestAt: now.AddSeconds(-2),
            Reason: CoordinatorStatusReason.Running));
        var globalLeakCoordinator = new FakeGlobalLeakCoordinator();
        var occupyCoordinator = new FakeOccupySeatCoordinator();
        var tomorrowCoordinator = new FakeTomorrowReservationCoordinator();
        tomorrowCoordinator.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Completed,
            "明日预约",
            "验证通过。",
            now.AddSeconds(-10),
            now,
            RequestCount: 2,
            LastRequestAt: now.AddSeconds(-1),
            Reason: CoordinatorStatusReason.TomorrowReservationSucceeded));
        var logs = new ActivityLogService();
        logs.Write(LogEntryKind.Info, "Grab", "抢座日志。");
        logs.Write(LogEntryKind.Info, "GlobalLeak", "全域日志。");
        logs.Write(LogEntryKind.Info, "Tomorrow", "明日日志。");
        logs.Write(LogEntryKind.Info, "Occupy", "占座日志。");
        logs.Write(LogEntryKind.Info, "Auth", "授权 code 已处理：secret-code。");

        var provider = new MobileControlStatusSnapshotProvider(
            runtimeState,
            runtimeState,
            workflowState,
            grabCoordinator,
            globalLeakCoordinator,
            tomorrowCoordinator,
            occupyCoordinator,
            logs,
            new FakeMobileControlTaskUiStateAccessor(new TimeSpan(20, 0, 0), "验证通过"),
            new FakeSettingsService(AppSettings.Default),
            timeProvider);

        var snapshot = await provider.CreateSnapshotAsync();

        Assert.True(snapshot.Cookie.IsAuthorized);
        Assert.Equal("授权链接", snapshot.Cookie.SourceText);
        Assert.Equal(50, snapshot.Cookie.ProgressValue);
        Assert.Equal("normal", snapshot.Cookie.ProgressLevel);
        Assert.DoesNotContain("secret-token", snapshot.Cookie.ToString(), StringComparison.Ordinal);
        Assert.Equal("自科 / 1", snapshot.Reservation.SummaryText);
        Assert.Equal("运行中", snapshot.Grab.StateText);
        Assert.Equal(5, snapshot.Grab.PollCount);
        Assert.Equal(3, snapshot.Grab.RequestCount);
        Assert.Equal("00:00:30", snapshot.Grab.RuntimeText);
        Assert.Contains(snapshot.Grab.Logs, line => line.Contains("Grab: 抢座日志", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Grab.Logs, line => line.Contains("Auth:", StringComparison.Ordinal));
        Assert.Equal("20:00:00", snapshot.Tomorrow.ScheduledTimeText);
        Assert.Equal("验证通过", snapshot.Tomorrow.VerificationText);
        Assert.Equal("01:00", snapshot.Occupy.ReReserveCountdownText);
    }

    [Fact]
    public async Task CreateSnapshotAsync_CalculatesCookieFixedDurationProgress()
    {
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var runtimeState = CreateRuntimeState(now);
        var workflowState = new ShellWorkflowState
        {
            CurrentCookieExpirationTime = now.AddMinutes(60)
        };
        var settings = AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                HomeCookieProgress = new HomeCookieProgressSettings(
                    HomeCookieProgressTimingMode.FixedCookieDuration,
                    120)
            }
        };
        var provider = CreateProvider(runtimeState, workflowState, timeProvider, settings);

        var snapshot = await provider.CreateSnapshotAsync();

        Assert.Equal(50, snapshot.Cookie.ProgressValue, precision: 2);
        Assert.Equal("normal", snapshot.Cookie.ProgressLevel);
    }

    [Fact]
    public async Task CreateSnapshotAsync_CalculatesCookieSoftwareRuntimeProgressAndResetsWhenIdentityChanges()
    {
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var runtimeState = CreateRuntimeState(now);
        var workflowState = new ShellWorkflowState
        {
            CurrentCookieExpirationTime = now.AddMinutes(60)
        };
        var settings = AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                HomeCookieProgress = new HomeCookieProgressSettings(
                    HomeCookieProgressTimingMode.SoftwareRuntimeDuration,
                    120)
            }
        };
        var provider = CreateProvider(runtimeState, workflowState, timeProvider, settings);

        var initial = await provider.CreateSnapshotAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        var sameCookie = await provider.CreateSnapshotAsync();
        runtimeState.Session = runtimeState.Session! with
        {
            Cookie = "Authorization=another-cookie; SERVERID=s"
        };
        workflowState.CurrentCookieExpirationTime = timeProvider.GetUtcNow().ToLocalTime().AddMinutes(60);
        var changedCookie = await provider.CreateSnapshotAsync();

        Assert.Equal(100, initial.Cookie.ProgressValue, precision: 2);
        Assert.Equal(50, sameCookie.Cookie.ProgressValue, precision: 2);
        Assert.Equal(100, changedCookie.Cookie.ProgressValue, precision: 2);
    }

    [Fact]
    public async Task CreateSnapshotAsync_CalculatesReservationFixedDurationProgress()
    {
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var runtimeState = CreateRuntimeState(now);
        var reservation = CreateReservation(now.AddMinutes(15));
        runtimeState.CurrentReservation = reservation;
        var workflowState = new ShellWorkflowState
        {
            CurrentReservation = reservation
        };
        var settings = AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                HomeReservationProgress = new HomeReservationProgressSettings(
                    HomeReservationProgressTimingMode.FixedReservationDuration,
                    30)
            }
        };
        var provider = CreateProvider(runtimeState, workflowState, timeProvider, settings);

        var snapshot = await provider.CreateSnapshotAsync();

        Assert.Equal(50, snapshot.Reservation.ProgressValue, precision: 2);
        Assert.Equal("normal", snapshot.Reservation.ProgressLevel);
    }

    [Fact]
    public async Task CreateSnapshotAsync_CalculatesReservationSoftwareRuntimeProgressAndResetsWhenIdentityChanges()
    {
        var now = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var runtimeState = CreateRuntimeState(now);
        var reservation = CreateReservation(now.AddMinutes(60));
        runtimeState.CurrentReservation = reservation;
        var workflowState = new ShellWorkflowState
        {
            CurrentReservation = reservation
        };
        var settings = AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with
            {
                HomeReservationProgress = new HomeReservationProgressSettings(
                    HomeReservationProgressTimingMode.SoftwareRuntimeDuration,
                    30)
            }
        };
        var provider = CreateProvider(runtimeState, workflowState, timeProvider, settings);

        var initial = await provider.CreateSnapshotAsync();
        timeProvider.Advance(TimeSpan.FromMinutes(30));
        var sameReservation = await provider.CreateSnapshotAsync();
        var changedReservation = CreateReservation(
            timeProvider.GetUtcNow().ToLocalTime().AddMinutes(60),
            token: "reservation-token-2",
            seatKey: "seat-2",
            seatName: "2");
        runtimeState.CurrentReservation = changedReservation;
        workflowState.CurrentReservation = changedReservation;
        var changed = await provider.CreateSnapshotAsync();

        Assert.Equal(100, initial.Reservation.ProgressValue, precision: 2);
        Assert.Equal(50, sameReservation.Reservation.ProgressValue, precision: 2);
        Assert.Equal(100, changed.Reservation.ProgressValue, precision: 2);
    }

    private static AppRuntimeState CreateRuntimeState(DateTimeOffset now)
    {
        return new AppRuntimeState
        {
            Session = new SessionCredentials(
                "Authorization=secret-token; SERVERID=s",
                SessionSource.QrCodeLink,
                now.AddMinutes(-10),
                true)
        };
    }

    private static ReservationInfo CreateReservation(
        DateTimeOffset expirationTime,
        string token = "reservation-token",
        string seatKey = "seat-1",
        string seatName = "1")
    {
        return new ReservationInfo(token, 1, "自科", seatKey, seatName, expirationTime);
    }

    private static MobileControlStatusSnapshotProvider CreateProvider(
        AppRuntimeState runtimeState,
        ShellWorkflowState workflowState,
        FakeTimeProvider timeProvider,
        AppSettings settings,
        IGrabSeatCoordinator? grabCoordinator = null,
        IGlobalLeakCoordinator? globalLeakCoordinator = null,
        ITomorrowReservationCoordinator? tomorrowCoordinator = null,
        IOccupySeatCoordinator? occupyCoordinator = null,
        IActivityLogService? activityLogService = null)
    {
        return new MobileControlStatusSnapshotProvider(
            runtimeState,
            runtimeState,
            workflowState,
            grabCoordinator ?? new FakeGrabSeatCoordinator(),
            globalLeakCoordinator ?? new FakeGlobalLeakCoordinator(),
            tomorrowCoordinator ?? new FakeTomorrowReservationCoordinator(),
            occupyCoordinator ?? new FakeOccupySeatCoordinator(),
            activityLogService ?? new ActivityLogService(),
            new FakeMobileControlTaskUiStateAccessor(null, string.Empty),
            new FakeSettingsService(settings),
            timeProvider);
    }

    private sealed class FakeMobileControlTaskUiStateAccessor(
        TimeSpan? scheduledTime,
        string verificationText) : IMobileControlTaskUiStateAccessor
    {
        public TimeSpan? TomorrowScheduledStartTime { get; } = scheduledTime;

        public string TomorrowVerificationText { get; } = verificationText;
    }
}
