using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class HomeDashboardViewModel : ViewModelBase
{
    private static readonly CultureInfo DashboardCulture = CultureInfo.GetCultureInfo("zh-CN");

    private readonly IActivityLogService _activityLogService;
    private readonly IAppThemeService _appThemeService;
    private readonly TimeProvider _timeProvider;

    private Func<bool>? _isAuthorized;
    private Func<bool>? _isGrabTaskActive;
    private Func<bool>? _isGlobalLeakTaskActive;
    private Func<bool>? _isTomorrowTaskActive;
    private Func<bool>? _isOccupyRunning;
    private Func<bool>? _hasLockedVenue;
    private Func<ReservationInfo?>? _currentReservation;
    private Action<string>? _setHomeReservationVenueText;
    private Func<DashboardMetrics, Task>? _saveDashboardMetricsAsync;
    private Action? _scheduleSettingsAutoSave;
    private int _historicalSuccessCount;
    private long _totalGuardSeconds;
    private DateTimeOffset? _guardTrackingStartedAt;
    private string? _homeReservationProgressReservationIdentity;
    private DateTimeOffset? _homeReservationProgressExpirationTime;
    private DateTimeOffset? _homeReservationProgressStartedAt;
    private IBrush _stateIdleBrush;
    private IBrush _stateRunningBrush;
    private IBrush _stateSuccessBrush;
    private IBrush _stateWarningBrush;
    private IBrush _stateFailureBrush;
    private IBrush _runningSoftBrush;
    private IBrush _successSoftBrush;
    private IBrush _warningSoftBrush;
    private IBrush _neutralSoftBrush;

    public HomeDashboardViewModel(
        IActivityLogService activityLogService,
        IAppThemeService appThemeService,
        TimeProvider timeProvider)
    {
        _activityLogService = activityLogService;
        _appThemeService = appThemeService;
        _timeProvider = timeProvider;

        var palette = _appThemeService.CurrentPalette;
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        _runningSoftBrush = palette.RunningSoftBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _warningSoftBrush = palette.WarningSoftBrush;
        _neutralSoftBrush = palette.NeutralSoftBrush;

        HomeHeroStatusBrush = _stateIdleBrush;
        HomeHeroStatusBackgroundBrush = _neutralSoftBrush;
        HomeReservationBadgeBrush = _stateIdleBrush;
        HomeReservationBadgeBackgroundBrush = _neutralSoftBrush;
        HomeReservationProgressBrush = _stateIdleBrush;
    }

    public string[] HomeReservationProgressTimingModes { get; } = ["固定预约到期时长", "软件运行时计算时长"];

    [ObservableProperty]
    private string homeGreetingTitleText = $"早安，{GetSystemUserDisplayName()}";

    [ObservableProperty]
    private string homeGreetingMessageText = "准备好开始今天的学习了吗？";

    [ObservableProperty]
    private string homeDateText = "--";

    [ObservableProperty]
    private string homeTimeText = "--:--:--";

    [ObservableProperty]
    private string homeHeroStatusText = "等待授权";

    [ObservableProperty]
    private string homeHeroStatusDetailText = "完成登录与场馆绑定后即可启用全部引擎。";

    [ObservableProperty]
    private IBrush homeHeroStatusBrush;

    [ObservableProperty]
    private IBrush homeHeroStatusBackgroundBrush;

    [ObservableProperty]
    private int homeHistoricalSuccessCount;

    [ObservableProperty]
    private string homeTotalGuardDurationText = "0 分钟";

    [ObservableProperty]
    private string homeEngineSummaryText = "等待授权";

    [ObservableProperty]
    private string homeMemoryUsageText = "--";

    [ObservableProperty]
    private string homeReservationSeatNumberText = "--";

    [ObservableProperty]
    private string homeReservationExpirationTimeText = "--:--:--";

    [ObservableProperty]
    private string homeReservationBadgeText = "暂无预约";

    [ObservableProperty]
    private IBrush homeReservationBadgeBrush;

    [ObservableProperty]
    private IBrush homeReservationBadgeBackgroundBrush;

    [ObservableProperty]
    private string homeReservationRemainingText = "--";

    [ObservableProperty]
    private double homeReservationProgressValue;

    [ObservableProperty]
    private IBrush homeReservationProgressBrush;

    [ObservableProperty]
    private int selectedHomeReservationProgressTimingModeIndex;

    public bool IsHomeReservationFixedProgressMode =>
        CurrentHomeReservationProgressTimingMode == HomeReservationProgressTimingMode.FixedReservationDuration;

    [ObservableProperty]
    private int homeReservationFixedDurationMinutes =
        HomeReservationProgressSettings.DefaultFixedDurationMinutes;

    public HomeReservationProgressTimingMode CurrentHomeReservationProgressTimingMode =>
        HomeReservationProgressSettings.NormalizeMode(
            (HomeReservationProgressTimingMode)Math.Clamp(
                SelectedHomeReservationProgressTimingModeIndex,
                0,
                HomeReservationProgressTimingModes.Length - 1));

    public void Configure(
        Func<bool> isAuthorized,
        Func<bool> isGrabTaskActive,
        Func<bool> isGlobalLeakTaskActive,
        Func<bool> isTomorrowTaskActive,
        Func<bool> isOccupyRunning,
        Func<bool> hasLockedVenue,
        Func<ReservationInfo?> currentReservation,
        Action<string> setHomeReservationVenueText,
        Func<DashboardMetrics, Task> saveDashboardMetricsAsync,
        Action scheduleSettingsAutoSave)
    {
        _isAuthorized = isAuthorized;
        _isGrabTaskActive = isGrabTaskActive;
        _isGlobalLeakTaskActive = isGlobalLeakTaskActive;
        _isTomorrowTaskActive = isTomorrowTaskActive;
        _isOccupyRunning = isOccupyRunning;
        _hasLockedVenue = hasLockedVenue;
        _currentReservation = currentReservation;
        _setHomeReservationVenueText = setHomeReservationVenueText;
        _saveDashboardMetricsAsync = saveDashboardMetricsAsync;
        _scheduleSettingsAutoSave = scheduleSettingsAutoSave;
    }

    public void ApplySettings(AppSettings settings)
    {
        var progress = HomeReservationProgressSettings.Normalize(settings.Ui.HomeReservationProgress);
        SelectedHomeReservationProgressTimingModeIndex = (int)progress.Mode;
        HomeReservationFixedDurationMinutes = progress.FixedDurationMinutes;
        _historicalSuccessCount = Math.Max(0, settings.Dashboard.SuccessfulReservationCount);
        _totalGuardSeconds = Math.Max(0, settings.Dashboard.TotalGuardSeconds);
        HomeHistoricalSuccessCount = _historicalSuccessCount;
        UpdatePresentation();
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        _runningSoftBrush = palette.RunningSoftBrush;
        _successSoftBrush = palette.SuccessSoftBrush;
        _warningSoftBrush = palette.WarningSoftBrush;
        _neutralSoftBrush = palette.NeutralSoftBrush;
        UpdatePresentation();
    }

    public void UpdatePresentation()
    {
        var now = GetCurrentTime();
        UpdateHeroPresentation(now);
        UpdateReservationCardPresentation(now);
        UpdateSystemInfoPresentation();
        UpdateGuardDurationPresentation(now);
    }

    public void UpdateClock()
    {
        var now = GetCurrentTime();
        UpdateHeroPresentation(now);
        UpdateReservationCardPresentation(now);
        UpdateGuardDurationPresentation(now);
    }

    public void UpdateHeroPresentation(DateTimeOffset now)
    {
        var localNow = now.ToLocalTime();
        HomeGreetingTitleText = BuildGreetingTitleText(localNow.Hour);
        HomeGreetingMessageText = BuildGreetingMessageText(localNow.Hour);
        HomeDateText = localNow.ToString("yyyy 年 MM 月 dd 日 dddd", DashboardCulture);
        HomeTimeText = localNow.ToString("HH:mm:ss", DashboardCulture);

        var (statusText, detailText, brush, backgroundBrush) = ResolveHomeHeroStatusPresentation();
        HomeHeroStatusText = statusText;
        HomeHeroStatusDetailText = detailText;
        HomeHeroStatusBrush = brush;
        HomeHeroStatusBackgroundBrush = backgroundBrush;
    }

    public void UpdateReservationCardPresentation(DateTimeOffset now)
    {
        var currentReservation = _currentReservation?.Invoke();
        if (currentReservation is null)
        {
            ClearReservationProgressTracking();
            HomeReservationSeatNumberText = "--";
            _setHomeReservationVenueText?.Invoke("当前暂无预约记录");
            HomeReservationExpirationTimeText = "--:--:--";
            HomeReservationBadgeText = "空闲中";
            HomeReservationBadgeBrush = _stateIdleBrush;
            HomeReservationBadgeBackgroundBrush = _neutralSoftBrush;
            HomeReservationRemainingText = "--";
            HomeReservationProgressValue = 0;
            HomeReservationProgressBrush = _stateIdleBrush;
            return;
        }

        EnsureReservationProgressTracking(currentReservation, now);
        var remaining = currentReservation.ExpirationTime - now;
        HomeReservationSeatNumberText = ExtractSeatNumberText(currentReservation.SeatName);
        _setHomeReservationVenueText?.Invoke(currentReservation.LibraryName);
        HomeReservationExpirationTimeText = currentReservation.ExpirationTime.ToString("HH:mm:ss", DashboardCulture);

        if (remaining <= TimeSpan.Zero)
        {
            HomeReservationBadgeText = "待刷新";
            HomeReservationBadgeBrush = _stateWarningBrush;
            HomeReservationBadgeBackgroundBrush = _warningSoftBrush;
            HomeReservationRemainingText = "已到期";
            HomeReservationProgressValue = 0;
            HomeReservationProgressBrush = _stateFailureBrush;
            return;
        }

        HomeReservationBadgeText = "生效中";
        HomeReservationBadgeBrush = _stateSuccessBrush;
        HomeReservationBadgeBackgroundBrush = _successSoftBrush;
        HomeReservationRemainingText = FormatReservationRemaining(remaining);
        HomeReservationProgressValue = CalculateReservationProgressValue(remaining, now);
        HomeReservationProgressBrush = ResolveProgressBrush(HomeReservationProgressValue);
    }

    public void UpdateSystemInfoPresentation()
    {
        HomeEngineSummaryText = BuildHomeEngineSummaryText();
        HomeMemoryUsageText = MeasureMemoryUsageText();
    }

    public void UpdateGuardTracking(DateTimeOffset timestamp)
    {
        if (IsAnyTaskActive())
        {
            _guardTrackingStartedAt ??= timestamp;
            UpdateGuardDurationPresentation(timestamp);
            return;
        }

        if (_guardTrackingStartedAt is null)
        {
            UpdateGuardDurationPresentation(timestamp);
            return;
        }

        _totalGuardSeconds = GetCurrentTotalGuardSeconds(timestamp);
        _guardTrackingStartedAt = null;
        UpdateGuardDurationPresentation(timestamp);
        _ = PersistDashboardMetricsAsync();
    }

    public async Task RecordSuccessfulReservationAsync()
    {
        _historicalSuccessCount++;
        HomeHistoricalSuccessCount = _historicalSuccessCount;
        await PersistDashboardMetricsAsync();
    }

    public void EnsureReservationProgressTracking(ReservationInfo reservation, DateTimeOffset observedAt)
    {
        var reservationIdentity = BuildReservationProgressIdentity(reservation);
        if (string.Equals(_homeReservationProgressReservationIdentity, reservationIdentity, StringComparison.Ordinal) &&
            _homeReservationProgressExpirationTime == reservation.ExpirationTime &&
            _homeReservationProgressStartedAt is not null)
        {
            return;
        }

        _homeReservationProgressReservationIdentity = reservationIdentity;
        _homeReservationProgressExpirationTime = reservation.ExpirationTime;
        _homeReservationProgressStartedAt = observedAt;
    }

    public void ClearReservationProgressTracking()
    {
        _homeReservationProgressReservationIdentity = null;
        _homeReservationProgressExpirationTime = null;
        _homeReservationProgressStartedAt = null;
    }

    partial void OnSelectedHomeReservationProgressTimingModeIndexChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, HomeReservationProgressTimingModes.Length - 1);
        if (normalized != value)
        {
            SelectedHomeReservationProgressTimingModeIndex = normalized;
            return;
        }

        OnPropertyChanged(nameof(IsHomeReservationFixedProgressMode));
        _scheduleSettingsAutoSave?.Invoke();
        UpdateReservationCardPresentation(GetCurrentTime());
    }

    partial void OnHomeReservationFixedDurationMinutesChanged(int value)
    {
        var normalized = HomeReservationProgressSettings.NormalizeFixedDurationMinutes(value);
        if (normalized != value)
        {
            HomeReservationFixedDurationMinutes = normalized;
            return;
        }

        _scheduleSettingsAutoSave?.Invoke();
        UpdateReservationCardPresentation(GetCurrentTime());
    }

    private (string StatusText, string DetailText, IBrush Brush, IBrush BackgroundBrush) ResolveHomeHeroStatusPresentation()
    {
        if (_isAuthorized?.Invoke() != true)
        {
            return ("等待授权", "完成登录与场馆绑定后即可启用全部引擎。", _stateWarningBrush, _warningSoftBrush);
        }

        var activeTaskCount = new[]
        {
            _isGrabTaskActive?.Invoke() == true,
            _isGlobalLeakTaskActive?.Invoke() == true,
            _isTomorrowTaskActive?.Invoke() == true,
            _isOccupyRunning?.Invoke() == true
        }.Count(static active => active);
        if (activeTaskCount >= 2)
        {
            return ("多任务协同中", "后台任务正在稳定运行，请保持程序常驻。", _stateRunningBrush, _runningSoftBrush);
        }

        if (_isGrabTaskActive?.Invoke() == true)
        {
            return ("抢座任务运行中", "已进入实时监控阶段，请保持程序常驻。", _stateRunningBrush, _runningSoftBrush);
        }

        if (_isGlobalLeakTaskActive?.Invoke() == true)
        {
            return ("全域捡漏运行中", "正在按轮扫描多个场馆，请保持程序常驻。", _stateRunningBrush, _runningSoftBrush);
        }

        if (_isTomorrowTaskActive?.Invoke() == true)
        {
            return ("明日预约运行中", "已进入预约等待或提交阶段，请保持程序常驻", _stateRunningBrush, _runningSoftBrush);
        }

        if (_isOccupyRunning?.Invoke() == true)
        {
            return ("占座守护运行中", "预约过期前会自动续占，请安心保持后台运行。", _stateSuccessBrush, _successSoftBrush);
        }

        if (_hasLockedVenue?.Invoke() == true)
        {
            return ("核心引擎就绪", "授权、场馆与本地配置均已准备完成。", _stateSuccessBrush, _successSoftBrush);
        }

        return ("等待绑定场馆", "当前已授权，下一步锁定一个常用场馆即可开始执行。", _stateWarningBrush, _warningSoftBrush);
    }

    private string BuildHomeEngineSummaryText()
    {
        if (_isAuthorized?.Invoke() != true)
        {
            return "等待授权";
        }

        var activeTasks = new List<string>(4);
        if (_isGrabTaskActive?.Invoke() == true)
        {
            activeTasks.Add("抢座运行中");
        }

        if (_isGlobalLeakTaskActive?.Invoke() == true)
        {
            activeTasks.Add("全域捡漏运行中");
        }

        if (_isTomorrowTaskActive?.Invoke() == true)
        {
            activeTasks.Add("明日预约运行中");
        }

        if (_isOccupyRunning?.Invoke() == true)
        {
            activeTasks.Add("占座守护运行中");
        }

        if (activeTasks.Count > 0)
        {
            return string.Join(" · ", activeTasks);
        }

        return _hasLockedVenue?.Invoke() == true ? "所有核心模块已就绪" : "等待绑定场馆";
    }

    private bool IsAnyTaskActive()
    {
        return _isGrabTaskActive?.Invoke() == true ||
               _isGlobalLeakTaskActive?.Invoke() == true ||
               _isTomorrowTaskActive?.Invoke() == true ||
               _isOccupyRunning?.Invoke() == true;
    }

    private void UpdateGuardDurationPresentation(DateTimeOffset timestamp)
    {
        HomeTotalGuardDurationText = FormatGuardDuration(GetCurrentTotalGuardSeconds(timestamp));
    }

    private long GetCurrentTotalGuardSeconds(DateTimeOffset timestamp)
    {
        var total = _totalGuardSeconds;
        if (_guardTrackingStartedAt is null)
        {
            return Math.Max(0, total);
        }

        var elapsed = timestamp - _guardTrackingStartedAt.Value;
        if (elapsed <= TimeSpan.Zero)
        {
            return Math.Max(0, total);
        }

        return Math.Max(0, total + (long)Math.Floor(elapsed.TotalSeconds));
    }

    private async Task PersistDashboardMetricsAsync()
    {
        if (_saveDashboardMetricsAsync is null)
        {
            return;
        }

        try
        {
            var totalGuardSeconds = GetCurrentTotalGuardSeconds(GetCurrentTime());
            await _saveDashboardMetricsAsync(new DashboardMetrics(
                _historicalSuccessCount,
                totalGuardSeconds));
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Dashboard", $"保存首页统计信息失败：{ex.Message}");
        }
    }

    private double CalculateReservationProgressValue(TimeSpan remaining, DateTimeOffset now)
    {
        var progressWindow = CurrentHomeReservationProgressTimingMode switch
        {
            HomeReservationProgressTimingMode.SoftwareRuntimeDuration =>
                ResolveSoftwareRuntimeProgressWindow(now),
            _ => TimeSpan.FromMinutes(HomeReservationFixedDurationMinutes)
        };

        if (progressWindow <= TimeSpan.Zero)
        {
            return 0;
        }

        return Math.Clamp(remaining.TotalSeconds / progressWindow.TotalSeconds * 100, 0, 100);
    }

    private IBrush ResolveProgressBrush(double progressValue)
    {
        if (progressValue < 10)
        {
            return _stateFailureBrush;
        }

        if (progressValue < 30)
        {
            return _stateWarningBrush;
        }

        return _stateSuccessBrush;
    }

    private TimeSpan ResolveSoftwareRuntimeProgressWindow(DateTimeOffset now)
    {
        var currentReservation = _currentReservation?.Invoke();
        if (currentReservation is null)
        {
            return TimeSpan.Zero;
        }

        EnsureReservationProgressTracking(currentReservation, now);
        return currentReservation.ExpirationTime - (_homeReservationProgressStartedAt ?? now);
    }

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }

    private static string BuildGreetingTitleText(int hour)
    {
        return hour switch
        {
            < 5 => $"夜深了，{GetSystemUserDisplayName()}",
            < 11 => $"早安，{GetSystemUserDisplayName()}",
            < 14 => $"中午好，{GetSystemUserDisplayName()}",
            < 18 => $"下午好，{GetSystemUserDisplayName()}",
            < 23 => $"晚上好，{GetSystemUserDisplayName()}",
            _ => $"夜深了，{GetSystemUserDisplayName()}"
        };
    }

    private static string BuildGreetingMessageText(int hour)
    {
        return hour switch
        {
            < 5 => "也别忘了给自己留一点休息时间。",
            < 11 => "准备好开始今天的学习了吗？",
            < 14 => "给今天的计划加把劲吧。",
            < 18 => "专注状态已经准备就绪。",
            < 23 => "把今天最后一段时间好好度过吧。",
            _ => "也别忘了给自己留一点休息时间。"
        };
    }

    private static string FormatGuardDuration(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return "0 分钟";
        }

        var duration = TimeSpan.FromSeconds(totalSeconds);
        if (duration.TotalHours >= 24)
        {
            return $"{Math.Max(1, (int)Math.Floor(duration.TotalHours))} 小时";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes:D2} 分";
        }

        return $"{Math.Max(1, duration.Minutes)} 分钟";
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

    private static string ExtractSeatNumberText(string seatName)
    {
        if (string.IsNullOrWhiteSpace(seatName))
        {
            return "--";
        }

        var digits = new string(seatName.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? seatName : digits;
    }

    private static string FormatReservationRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
        {
            return remaining.ToString(@"hh\:mm\:ss", DashboardCulture);
        }

        return remaining.ToString(@"mm\:ss", DashboardCulture);
    }

    private static string MeasureMemoryUsageText()
    {
        using var process = Process.GetCurrentProcess();
        var memory = process.WorkingSet64 / 1024d / 1024d;
        return $"{memory:0.#} MB";
    }

    private static string GetSystemUserDisplayName()
    {
        return SystemUserDisplayNameResolver.GetCurrentDisplayName();
    }
}
