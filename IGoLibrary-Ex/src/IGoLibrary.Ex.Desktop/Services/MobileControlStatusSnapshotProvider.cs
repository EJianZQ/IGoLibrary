using System.Globalization;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlStatusSnapshotProvider(
    ISessionState sessionState,
    IReservationState reservationState,
    ShellWorkflowState workflowState,
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    IActivityLogService activityLogService,
    IMobileControlTaskUiStateAccessor taskUiStateAccessor,
    ISettingsService settingsService,
    TimeProvider timeProvider) : IMobileControlStatusSnapshotProvider
{
    private const int MaxLogLines = 100;
    private const string ProgressLevelIdle = "idle";
    private const string ProgressLevelNormal = "normal";
    private const string ProgressLevelWarning = "warning";
    private const string ProgressLevelDanger = "danger";

    private readonly object _progressGate = new();
    private string? _cookieProgressIdentity;
    private DateTimeOffset? _cookieProgressExpirationTime;
    private DateTimeOffset? _cookieProgressStartedAt;
    private string? _reservationProgressIdentity;
    private DateTimeOffset? _reservationProgressExpirationTime;
    private DateTimeOffset? _reservationProgressStartedAt;

    public async Task<MobileControlStatusSnapshot> CreateSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().ToLocalTime();
        var settings = await settingsService.LoadAsync(cancellationToken);
        var cookieProgressSettings = HomeCookieProgressSettings.Normalize(settings.Ui.HomeCookieProgress);
        var reservationProgressSettings = HomeReservationProgressSettings.Normalize(settings.Ui.HomeReservationProgress);
        var reservation = workflowState.CurrentReservation ?? reservationState.CurrentReservation;
        var grab = grabSeatCoordinator.GetStatus();
        var globalLeak = globalLeakCoordinator.GetStatus();
        var tomorrow = tomorrowReservationCoordinator.GetStatus();
        var occupy = occupySeatCoordinator.GetStatus();

        return new MobileControlStatusSnapshot(
            now,
            FormatDateTime(now),
            CreateCookieSnapshot(now, cookieProgressSettings),
            CreateReservationSnapshot(reservation, now, reservationProgressSettings),
            new MobileControlGrabTaskSnapshot(
                GetStateText(grab),
                TrimSentenceEnding(grab.Message),
                IsActive(grab),
                grab.PollCount,
                grab.RequestCount,
                FormatLastRequest(grab.LastRequestAt, now),
                FormatRuntime(grab, now),
                GetLogs("Grab")),
            new MobileControlGlobalLeakTaskSnapshot(
                GetStateText(globalLeak),
                TrimSentenceEnding(globalLeak.Message),
                IsActive(globalLeak),
                globalLeak.PollCount,
                globalLeak.RequestCount,
                FormatLastRequest(globalLeak.LastRequestAt, now),
                FormatRuntime(globalLeak, now),
                GetLogs("GlobalLeak")),
            new MobileControlTomorrowTaskSnapshot(
                GetStateText(tomorrow),
                TrimSentenceEnding(tomorrow.Message),
                IsActive(tomorrow),
                FormatScheduledTime(taskUiStateAccessor.TomorrowScheduledStartTime),
                tomorrow.RequestCount,
                FormatLastRequest(tomorrow.LastRequestAt, now),
                string.IsNullOrWhiteSpace(taskUiStateAccessor.TomorrowVerificationText)
                    ? "尚未执行明日预约"
                    : taskUiStateAccessor.TomorrowVerificationText,
                FormatRuntime(tomorrow, now),
                GetLogs("Tomorrow")),
            new MobileControlOccupyTaskSnapshot(
                GetStateText(occupy),
                TrimSentenceEnding(occupy.Message),
                IsActive(occupy),
                reservation is null ? "无" : FormatDateTime(reservation.ExpirationTime),
                reservation is null
                    ? "等待建立预约状态"
                    : FormatDuration(ReservationTimeHelper.GetReReserveTriggerCountdown(reservation.ExpirationTime, now)),
                GetLogs("Occupy")));
    }

    private MobileControlCookieSnapshot CreateCookieSnapshot(
        DateTimeOffset now,
        HomeCookieProgressSettings progressSettings)
    {
        var session = sessionState.Session;
        if (session is null)
        {
            ClearCookieProgressTracking();
            return new MobileControlCookieSnapshot(false, "未登录", "无", "无", "无", "无", 0, ProgressLevelIdle);
        }

        var expirationTime = workflowState.CurrentCookieExpirationTime;
        var (progressValue, progressLevel) = CalculateCookieProgress(
            expirationTime,
            session.Cookie,
            now,
            progressSettings);
        return new MobileControlCookieSnapshot(
            true,
            expirationTime is not null && expirationTime <= now ? "已过期" : "已登录",
            GetSessionSourceText(session.Source),
            FormatDateTime(session.SavedAt),
            expirationTime is null ? "未知" : FormatDateTime(expirationTime.Value),
            expirationTime is null ? "未知" : FormatDuration(expirationTime.Value - now),
            progressValue,
            progressLevel);
    }

    private MobileControlReservationSnapshot CreateReservationSnapshot(
        ReservationInfo? reservation,
        DateTimeOffset now,
        HomeReservationProgressSettings progressSettings)
    {
        if (reservation is null)
        {
            ClearReservationProgressTracking();
            return new MobileControlReservationSnapshot(false, "暂无预约", "", "", "无", "无", 0, ProgressLevelIdle);
        }

        var (progressValue, progressLevel) = CalculateReservationProgress(reservation, now, progressSettings);
        return new MobileControlReservationSnapshot(
            true,
            $"{reservation.LibraryName} / {reservation.SeatName}",
            reservation.LibraryName,
            reservation.SeatName,
            FormatDateTime(reservation.ExpirationTime),
            FormatDuration(reservation.ExpirationTime - now),
            progressValue,
            progressLevel);
    }

    private (double Value, string Level) CalculateCookieProgress(
        DateTimeOffset? expirationTime,
        string? cookieIdentity,
        DateTimeOffset now,
        HomeCookieProgressSettings settings)
    {
        if (expirationTime is null)
        {
            ClearCookieProgressTracking();
            return (0, ProgressLevelIdle);
        }

        if (expirationTime <= now)
        {
            ClearCookieProgressTracking();
            return (0, ProgressLevelDanger);
        }

        TimeSpan progressWindow;
        lock (_progressGate)
        {
            EnsureCookieProgressTracking(expirationTime.Value, cookieIdentity, now);
            progressWindow = settings.Mode switch
            {
                HomeCookieProgressTimingMode.SoftwareRuntimeDuration =>
                    expirationTime.Value - (_cookieProgressStartedAt ?? now),
                _ => TimeSpan.FromMinutes(settings.FixedDurationMinutes)
            };
        }

        return CalculateProgress(expirationTime.Value - now, progressWindow);
    }

    private (double Value, string Level) CalculateReservationProgress(
        ReservationInfo reservation,
        DateTimeOffset now,
        HomeReservationProgressSettings settings)
    {
        if (reservation.ExpirationTime <= now)
        {
            ClearReservationProgressTracking();
            return (0, ProgressLevelDanger);
        }

        TimeSpan progressWindow;
        lock (_progressGate)
        {
            EnsureReservationProgressTracking(reservation, now);
            progressWindow = settings.Mode switch
            {
                HomeReservationProgressTimingMode.SoftwareRuntimeDuration =>
                    reservation.ExpirationTime - (_reservationProgressStartedAt ?? now),
                _ => TimeSpan.FromMinutes(settings.FixedDurationMinutes)
            };
        }

        return CalculateProgress(reservation.ExpirationTime - now, progressWindow);
    }

    private static (double Value, string Level) CalculateProgress(TimeSpan remaining, TimeSpan progressWindow)
    {
        if (progressWindow <= TimeSpan.Zero)
        {
            return (0, ProgressLevelDanger);
        }

        var value = Math.Clamp(remaining.TotalSeconds / progressWindow.TotalSeconds * 100, 0, 100);
        return (Math.Round(value, 2), ResolveProgressLevel(value));
    }

    private void EnsureCookieProgressTracking(
        DateTimeOffset expirationTime,
        string? cookieIdentity,
        DateTimeOffset observedAt)
    {
        cookieIdentity = BuildCookieProgressIdentity(expirationTime, cookieIdentity);
        if (string.Equals(_cookieProgressIdentity, cookieIdentity, StringComparison.Ordinal) &&
            _cookieProgressExpirationTime == expirationTime &&
            _cookieProgressStartedAt is not null)
        {
            return;
        }

        _cookieProgressIdentity = cookieIdentity;
        _cookieProgressExpirationTime = expirationTime;
        _cookieProgressStartedAt = observedAt;
    }

    private void EnsureReservationProgressTracking(
        ReservationInfo reservation,
        DateTimeOffset observedAt)
    {
        var reservationIdentity = BuildReservationProgressIdentity(reservation);
        if (string.Equals(_reservationProgressIdentity, reservationIdentity, StringComparison.Ordinal) &&
            _reservationProgressExpirationTime == reservation.ExpirationTime &&
            _reservationProgressStartedAt is not null)
        {
            return;
        }

        _reservationProgressIdentity = reservationIdentity;
        _reservationProgressExpirationTime = reservation.ExpirationTime;
        _reservationProgressStartedAt = observedAt;
    }

    private void ClearCookieProgressTracking()
    {
        lock (_progressGate)
        {
            _cookieProgressIdentity = null;
            _cookieProgressExpirationTime = null;
            _cookieProgressStartedAt = null;
        }
    }

    private void ClearReservationProgressTracking()
    {
        lock (_progressGate)
        {
            _reservationProgressIdentity = null;
            _reservationProgressExpirationTime = null;
            _reservationProgressStartedAt = null;
        }
    }

    private static string ResolveProgressLevel(double progressValue)
    {
        if (progressValue < 10)
        {
            return ProgressLevelDanger;
        }

        return progressValue < 30
            ? ProgressLevelWarning
            : ProgressLevelNormal;
    }

    private static string BuildCookieProgressIdentity(DateTimeOffset expirationTime, string? cookieIdentity)
    {
        var normalizedCookieIdentity = cookieIdentity?.Trim();
        return string.IsNullOrWhiteSpace(normalizedCookieIdentity)
            ? expirationTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)
            : normalizedCookieIdentity;
    }

    private static string BuildReservationProgressIdentity(ReservationInfo reservation)
    {
        return string.Join(
            "\u001F",
            reservation.LibraryId.ToString(CultureInfo.InvariantCulture),
            reservation.LibraryName,
            reservation.SeatKey,
            reservation.SeatName);
    }

    private IReadOnlyList<string> GetLogs(params string[] categories)
    {
        var categorySet = categories.ToHashSet(StringComparer.Ordinal);
        return activityLogService.Entries
            .Where(entry => categorySet.Contains(entry.Category))
            .TakeLast(MaxLogLines)
            .Select(static entry => $"[{entry.Timestamp:HH:mm:ss}] {entry.Category}: {TrimSentenceEnding(entry.Message)}")
            .ToArray();
    }

    private static string GetStateText(CoordinatorStatus status)
    {
        return status.State switch
        {
            CoordinatorTaskState.Starting => "启动中",
            CoordinatorTaskState.Running => "运行中",
            CoordinatorTaskState.Stopping => "停止中",
            CoordinatorTaskState.Completed when status.Reason == CoordinatorStatusReason.Stopped => "已停止",
            CoordinatorTaskState.Completed => "已完成",
            CoordinatorTaskState.Failed => "异常",
            _ => "未运行"
        };
    }

    private static string FormatRuntime(CoordinatorStatus status, DateTimeOffset now)
    {
        if (status.StartedAt is null)
        {
            return "00:00:00";
        }

        var end = IsActive(status)
            ? now
            : status.LastUpdatedAt ?? now;
        return FormatElapsedClock(end - status.StartedAt.Value);
    }

    private static string FormatLastRequest(DateTimeOffset? lastRequestAt, DateTimeOffset now)
    {
        if (lastRequestAt is null)
        {
            return "无";
        }

        var elapsed = now - lastRequestAt.Value;
        if (elapsed <= TimeSpan.Zero)
        {
            return "刚刚";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes))} 分钟前";
        }

        return lastRequestAt.Value.ToString("HH:mm:ss");
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatScheduledTime(TimeSpan? value)
    {
        return value is null ? "未设置" : value.Value.ToString(@"hh\:mm\:ss");
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "00:00";
        }

        if (value.TotalHours >= 1)
        {
            return $"{Math.Max(0, (int)value.TotalHours):D2}:{value.Minutes:D2}:{value.Seconds:D2}";
        }

        return $"{Math.Max(0, value.Minutes):D2}:{value.Seconds:D2}";
    }

    private static string FormatElapsedClock(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "00:00:00";
        }

        return $"{Math.Max(0, (int)value.TotalHours):D2}:{value.Minutes:D2}:{value.Seconds:D2}";
    }

    private static string GetSessionSourceText(SessionSource source)
    {
        return source switch
        {
            SessionSource.QrCodeLink => "授权链接",
            SessionSource.ManualCookie => "手动 Cookie",
            SessionSource.Restored => "本地恢复",
            _ => "未知"
        };
    }

    private static bool IsActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private static string TrimSentenceEnding(string message)
    {
        return string.IsNullOrEmpty(message)
            ? message
            : message.TrimEnd('。', '.');
    }
}
