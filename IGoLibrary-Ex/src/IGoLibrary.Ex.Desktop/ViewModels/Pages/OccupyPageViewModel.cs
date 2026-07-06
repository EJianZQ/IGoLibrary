using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class OccupyPageViewModel : ViewModelBase
{
    private readonly IOccupySeatCoordinator _occupySeatCoordinator;
    private readonly IReservationWorkflowService _reservationWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    private Func<bool>? _isInitialized;
    private Func<bool>? _isLoadingSettings;
    private Action? _scheduleSettingsAutoSave;
    private Action<ReservationInfo?>? _reservationChanged;
    private Func<Task>? _recordSuccessfulReservationAsync;
    private Action<CoordinatorStatus>? _statusApplied;
    private bool _statusSubscribed;
    private bool _isAutoReleaseRefreshingReservation;
    private string? _lastAutoReleaseFailedReservationToken;
    private DateTimeOffset? _lastAutoReleaseFailedAt;
    private DateTimeOffset? _lastRecordedOccupySuccessAt;

    public OccupyPageViewModel(
        IOccupySeatCoordinator occupySeatCoordinator,
        IReservationWorkflowService reservationWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        TimeProvider timeProvider)
    {
        _occupySeatCoordinator = occupySeatCoordinator;
        _reservationWorkflowService = reservationWorkflowService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    public string[] OccupyCheckIntervalModes { get; } = ["固定间隔 10 秒", "随机 10~20 秒"];

    [ObservableProperty]
    private ReservationInfo? currentReservation;

    public bool HasCurrentReservation => CurrentReservation is not null;

    public bool HasNoCurrentReservation => !HasCurrentReservation;

    public bool CanCancelCurrentReservation => CurrentReservation is not null && !IsCancellingCurrentReservation;

    [ObservableProperty]
    private bool isCancellingCurrentReservation;

    [ObservableProperty]
    private string reservationSummary = "暂无预约";

    [ObservableProperty]
    private string reservationHeroTitle = "暂无预约";

    [ObservableProperty]
    private string reservationExpiryText = "到期：--:--:--";

    [ObservableProperty]
    private string reservationCountdownText = "等待建立预约状态";

    [ObservableProperty]
    private string occupyStatusText = "未运行";

    [ObservableProperty]
    private bool isOccupyRunning;

    public bool IsOccupyStopped => !IsOccupyRunning;

    [ObservableProperty]
    private int reReserveDelaySeconds = 60;

    [ObservableProperty]
    private int selectedOccupyCheckIntervalModeIndex;

    [ObservableProperty]
    private bool autoReleaseReservationEnabled;

    [ObservableProperty]
    private int autoReleaseLeadSeconds = AutoReleaseTaskSettings.DefaultLeadSeconds;

    public bool IsAutoReleaseSuppressedByOccupy => IsOccupyRunning;

    public string AutoReleaseStatusText
    {
        get
        {
            if (!AutoReleaseReservationEnabled)
            {
                return "自动退座未启用";
            }

            if (IsAutoReleaseSuppressedByOccupy)
            {
                return "占座运行中，自动退座已暂停";
            }

            return HasCurrentReservation
                ? $"已启用：将在到期前 {AutoReleaseLeadSeconds} 秒自动退座"
                : $"已启用：当前暂无预约，到期前 {AutoReleaseLeadSeconds} 秒将自动退座";
        }
    }

    public void ConfigureOrchestration(
        Func<bool> isInitialized,
        Func<bool> isLoadingSettings,
        Action scheduleSettingsAutoSave,
        Action<ReservationInfo?> reservationChanged,
        Func<Task> recordSuccessfulReservationAsync,
        Action<CoordinatorStatus> statusApplied)
    {
        _isInitialized = isInitialized;
        _isLoadingSettings = isLoadingSettings;
        _scheduleSettingsAutoSave = scheduleSettingsAutoSave;
        _reservationChanged = reservationChanged;
        _recordSuccessfulReservationAsync = recordSuccessfulReservationAsync;
        _statusApplied = statusApplied;
    }

    public void InitializeStatus()
    {
        if (!_statusSubscribed)
        {
            _statusSubscribed = true;
            _occupySeatCoordinator.StatusChanged += OnOccupyStatusChanged;
        }

        ApplyOccupyStatus(_occupySeatCoordinator.GetStatus());
    }

    public void ApplySettings(AppSettings settings)
    {
        AutoReleaseReservationEnabled = settings.Tasks.AutoRelease.Enabled;
        AutoReleaseLeadSeconds = AutoReleaseTaskSettings.NormalizeLeadSeconds(settings.Tasks.AutoRelease.LeadSeconds);
    }

    public void ApplyStatus(CoordinatorStatus status)
    {
        ApplyOccupyStatus(status);
    }

    public Task StartAsync(OccupySeatPlan plan, CancellationToken cancellationToken = default)
    {
        return _occupySeatCoordinator.StartAsync(plan, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _occupySeatCoordinator.StopAsync(cancellationToken);
    }

    public Task<ReservationOperationResult> RefreshReservationOperationAsync(CancellationToken cancellationToken = default)
    {
        return _reservationWorkflowService.RefreshReservationAsync(cancellationToken);
    }

    public Task<ReservationOperationResult> CancelCurrentReservationOperationAsync(
        ReservationInfo reservation,
        bool stopOccupyFirst,
        CancellationToken cancellationToken = default)
    {
        return _reservationWorkflowService.CancelCurrentReservationAsync(
            reservation,
            stopOccupyFirst,
            cancellationToken);
    }

    public void QueueAutoReleaseReservationRefresh()
    {
        if (_isLoadingSettings?.Invoke() == true ||
            _isInitialized?.Invoke() != true ||
            !AutoReleaseReservationEnabled ||
            IsAutoReleaseSuppressedByOccupy ||
            _isAutoReleaseRefreshingReservation)
        {
            return;
        }

        _ = RefreshReservationForAutoReleaseAsync();
    }

    public void QueueAutoReleaseCheck()
    {
        if (_isLoadingSettings?.Invoke() == true ||
            _isInitialized?.Invoke() != true)
        {
            return;
        }

        _ = TryAutoReleaseCurrentReservationAsync();
    }

    public void OnCountdownTick()
    {
        UpdateReservationCountdown();
        QueueAutoReleaseCheck();
    }

    public void TryRecordOccupySuccess(DateTimeOffset timestamp)
    {
        if (_lastRecordedOccupySuccessAt == timestamp)
        {
            return;
        }

        _lastRecordedOccupySuccessAt = timestamp;
        if (_recordSuccessfulReservationAsync is not null)
        {
            _ = _recordSuccessfulReservationAsync();
        }
    }

    public void UpdateReservationPresentation(ReservationInfo? info)
    {
        var previousReservationToken = CurrentReservation?.ReservationToken;
        CurrentReservation = info;
        if (info is null ||
            !string.Equals(previousReservationToken, info.ReservationToken, StringComparison.Ordinal))
        {
            ClearAutoReleaseFailure();
        }

        OnPropertyChanged(nameof(HasCurrentReservation));
        OnPropertyChanged(nameof(HasNoCurrentReservation));
        OnPropertyChanged(nameof(CanCancelCurrentReservation));
        OnPropertyChanged(nameof(AutoReleaseStatusText));

        if (info is null)
        {
            ReservationSummary = "暂无预约";
            ReservationHeroTitle = "暂无预约";
            ReservationExpiryText = "到期：--:--:--";
            ReservationCountdownText = "等待建立预约状态";
            _reservationChanged?.Invoke(null);
            QueueAutoReleaseCheck();
            return;
        }

        ReservationSummary = $"{info.LibraryName} / {info.SeatName} / 到期 {info.ExpirationTime:HH:mm:ss}";
        ReservationHeroTitle = $"{info.LibraryName} · {info.SeatName}";
        UpdateReservationCountdown();
        _reservationChanged?.Invoke(info);
        QueueAutoReleaseCheck();
    }

    partial void OnAutoReleaseReservationEnabledChanged(bool value)
    {
        _scheduleSettingsAutoSave?.Invoke();
        OnPropertyChanged(nameof(AutoReleaseStatusText));
        QueueAutoReleaseReservationRefresh();
        QueueAutoReleaseCheck();
    }

    partial void OnAutoReleaseLeadSecondsChanged(int value)
    {
        var normalized = AutoReleaseTaskSettings.NormalizeLeadSeconds(value);
        if (normalized != value)
        {
            AutoReleaseLeadSeconds = normalized;
            return;
        }

        _scheduleSettingsAutoSave?.Invoke();
        OnPropertyChanged(nameof(AutoReleaseStatusText));
        QueueAutoReleaseReservationRefresh();
        QueueAutoReleaseCheck();
    }

    partial void OnIsOccupyRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOccupyStopped));
        OnPropertyChanged(nameof(IsAutoReleaseSuppressedByOccupy));
        OnPropertyChanged(nameof(AutoReleaseStatusText));
        if (!value)
        {
            QueueAutoReleaseReservationRefresh();
            QueueAutoReleaseCheck();
        }
    }

    partial void OnIsCancellingCurrentReservationChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancelCurrentReservation));
        OnPropertyChanged(nameof(AutoReleaseStatusText));
    }

    [RelayCommand]
    private Task RefreshReservation()
    {
        return RefreshReservationAsync(showNotificationOnError: true);
    }

    public async Task RefreshReservationAsync(bool showNotificationOnError)
    {
        try
        {
            var result = await RefreshReservationOperationAsync();
            if (!result.HasSession)
            {
                UpdateReservationPresentation(null);
                return;
            }

            if (result.Succeeded)
            {
                UpdateReservationPresentation(result.Reservation);
            }
            else if (showNotificationOnError)
            {
                await _notificationService.ShowWarningAsync("刷新预约状态失败", result.FailureMessage ?? "接口未返回预约状态");
            }
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"刷新预约状态失败：{ex.Message}");
            if (showNotificationOnError)
            {
                await _notificationService.ShowWarningAsync("刷新预约状态失败", ex.Message);
            }
        }
    }

    [RelayCommand]
    private Task CancelCurrentReservation()
    {
        return CancelCurrentReservationCoreAsync(
            isAutomatic: false,
            stopOccupyFirst: IsOccupyRunning);
    }

    [RelayCommand]
    private async Task StartOccupyAsync()
    {
        try
        {
            var plan = new OccupySeatPlan(
                TimeSpan.FromSeconds(Math.Max(1, ReReserveDelaySeconds)),
                (OccupyCheckIntervalMode)SelectedOccupyCheckIntervalModeIndex);
            IsOccupyRunning = true;
            await StartAsync(plan);
        }
        catch (Exception ex)
        {
            ApplyOccupyStatus(_occupySeatCoordinator.GetStatus());
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"启动占座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动占座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopOccupyAsync()
    {
        try
        {
            await StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Occupy", $"停止占座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止占座失败", ex.Message);
        }
    }

    private async Task CancelCurrentReservationCoreAsync(
        bool isAutomatic,
        bool stopOccupyFirst)
    {
        if (CurrentReservation is null || IsCancellingCurrentReservation)
        {
            return;
        }

        var reservation = CurrentReservation;
        IsCancellingCurrentReservation = true;

        try
        {
            var result = await CancelCurrentReservationOperationAsync(
                reservation,
                stopOccupyFirst);
            if (!result.HasSession)
            {
                if (isAutomatic)
                {
                    RecordAutoReleaseFailure(reservation);
                    _activityLogService.Write(LogEntryKind.Warning, "AutoRelease", "自动退座失败：当前会话已失效。");
                }
                else
                {
                    await _notificationService.ShowWarningAsync("未登录", "当前会话已失效，请重新授权后再操作");
                }

                return;
            }

            if (!result.RemoteSucceeded)
            {
                if (isAutomatic)
                {
                    RecordAutoReleaseFailure(reservation);
                    _activityLogService.Write(LogEntryKind.Warning, "AutoRelease", $"{reservation.SeatName} 自动退座失败，接口未返回成功结果。");
                }
                else
                {
                    _activityLogService.Write(LogEntryKind.Warning, "Occupy", $"{reservation.SeatName} 取消预约失败，接口未返回成功结果。");
                    await _notificationService.ShowWarningAsync("取消预约失败", "接口未返回成功结果，请稍后重试");
                }

                return;
            }

            ClearAutoReleaseFailure();
            UpdateReservationPresentation(result.Reservation);
            if (isAutomatic)
            {
                _activityLogService.Write(LogEntryKind.Success, "AutoRelease", $"{reservation.SeatName} 已自动退座。");
                try
                {
                    await _notificationService.ShowSuccessAsync("已自动退座", $"{reservation.SeatName} 已自动取消预约");
                }
                catch (Exception ex)
                {
                    _activityLogService.Write(LogEntryKind.Warning, "AutoRelease", $"自动退座成功通知失败：{ex.Message}");
                }
            }
            else
            {
                _activityLogService.Write(LogEntryKind.Success, "Occupy", $"{reservation.SeatName} 已手动取消预约。");
                await _notificationService.ShowSuccessAsync("已取消预约", $"{reservation.SeatName} 已取消预约");
            }
        }
        catch (Exception ex)
        {
            if (isAutomatic)
            {
                RecordAutoReleaseFailure(reservation);
                _activityLogService.Write(LogEntryKind.Warning, "AutoRelease", $"自动退座失败：{ex.Message}");
            }
            else
            {
                _activityLogService.Write(LogEntryKind.Error, "Occupy", $"取消预约失败：{ex.Message}");
                await _notificationService.ShowWarningAsync("取消预约失败", ex.Message);
            }
        }
        finally
        {
            IsCancellingCurrentReservation = false;
        }
    }

    private async Task RefreshReservationForAutoReleaseAsync()
    {
        if (_isAutoReleaseRefreshingReservation)
        {
            return;
        }

        _isAutoReleaseRefreshingReservation = true;
        try
        {
            await RefreshReservationAsync(showNotificationOnError: false);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "AutoRelease", $"自动退座刷新当前预约失败：{ex.Message}");
        }
        finally
        {
            _isAutoReleaseRefreshingReservation = false;
        }
    }

    private async Task TryAutoReleaseCurrentReservationAsync()
    {
        var now = GetCurrentTime();
        if (!AutoReleaseReservationPolicy.ShouldCancel(
                CurrentReservation,
                AutoReleaseReservationEnabled,
                AutoReleaseLeadSeconds,
                IsCancellingCurrentReservation,
                IsAutoReleaseSuppressedByOccupy,
                _lastAutoReleaseFailedReservationToken,
                _lastAutoReleaseFailedAt,
                now))
        {
            OnPropertyChanged(nameof(AutoReleaseStatusText));
            return;
        }

        await CancelCurrentReservationCoreAsync(
            isAutomatic: true,
            stopOccupyFirst: false);
    }

    private void RecordAutoReleaseFailure(ReservationInfo reservation)
    {
        _lastAutoReleaseFailedReservationToken = reservation.ReservationToken;
        _lastAutoReleaseFailedAt = GetCurrentTime();
    }

    private void ClearAutoReleaseFailure()
    {
        _lastAutoReleaseFailedReservationToken = null;
        _lastAutoReleaseFailedAt = null;
    }

    private void OnOccupyStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() => ApplyOccupyStatus(status));
    }

    private void ApplyOccupyStatus(CoordinatorStatus status)
    {
        OccupyStatusText = status.Message;
        IsOccupyRunning = IsTaskActive(status);
        _statusApplied?.Invoke(status);
    }

    private void UpdateReservationCountdown()
    {
        if (CurrentReservation is null)
        {
            ReservationExpiryText = "到期：--:--:--";
            ReservationCountdownText = "等待建立预约状态";
            return;
        }

        ReservationExpiryText = $"到期：{CurrentReservation.ExpirationTime:HH:mm:ss}";

        var remaining = CurrentReservation.ExpirationTime - GetCurrentTime();
        if (remaining <= TimeSpan.Zero)
        {
            ReservationCountdownText = "倒计时：已到期，等待刷新";
            return;
        }

        var countdown = remaining >= TimeSpan.FromHours(1)
            ? remaining.ToString(@"hh\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
        ReservationCountdownText = $"倒计时：{countdown}";
    }

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }

    private static bool IsTaskActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }
}
