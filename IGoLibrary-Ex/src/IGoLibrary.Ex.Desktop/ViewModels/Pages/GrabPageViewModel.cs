using Avalonia.Media;
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

public sealed partial class GrabPageViewModel : ViewModelBase
{
    private const int OptimalStrategyReminderSeatThreshold = 5;
    private static readonly TimeSpan DefaultScheduledStartTime = GrabTaskSettings.Default.DefaultScheduledStartTime;

    private readonly IGrabSeatCoordinator _grabSeatCoordinator;
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IAppThemeService _appThemeService;
    private readonly IGrabStrategyReminderDialogService _strategyReminderDialogService;
    private readonly TimeProvider _timeProvider;

    private Func<bool>? _isInitialized;
    private Func<bool>? _isLoadingSettings;
    private Func<bool> _isOptimalStrategyReminderEnabled =
        static () => GrabTaskSettings.Default.OptimalStrategyReminderEnabled;
    private Func<Task> _flushPendingSystemSettingsAsync = static () => Task.CompletedTask;
    private Action<bool> _applyPersistedOptimalStrategyReminder = static _ => { };
    private Func<LibrarySummary?>? _selectedLibrary;
    private Func<int>? _seatCount;
    private Func<IReadOnlyList<SeatReference>>? _selectedSeats;
    private Func<Task>? _refreshSeatsAsync;
    private Action? _beginSeatSelectionDraft;
    private Action? _commitSeatSelection;
    private Action? _restoreCommittedSeatSelection;
    private Func<Task>? _refreshSuccessReservationAsync;
    private Func<Task>? _recordSuccessfulReservationAsync;
    private Action<CoordinatorStatus>? _statusApplied;
    private bool _statusSubscribed;
    private CoordinatorTaskState _grabTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _grabStatusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _grabLastRequestAt;
    private DateTimeOffset? _grabRuntimeStartedAt;
    private DateTimeOffset? _lastRecordedGrabSuccessAt;
    private TimeSpan _grabScheduledStartDefault = DefaultScheduledStartTime;
    private TimeSpan? _pendingGrabScheduledStartDefault;
    private CancellationTokenSource? _grabScheduledStartDefaultAutoSaveCts;
    private IBrush _stateIdleBrush;
    private IBrush _stateRunningBrush;
    private IBrush _stateSuccessBrush;
    private IBrush _stateWarningBrush;
    private IBrush _stateFailureBrush;

    public GrabPageViewModel(
        IGrabSeatCoordinator grabSeatCoordinator,
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService,
        IGrabStrategyReminderDialogService strategyReminderDialogService,
        TimeProvider timeProvider)
    {
        _grabSeatCoordinator = grabSeatCoordinator;
        _settingsWorkflowService = settingsWorkflowService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _appThemeService = appThemeService;
        _strategyReminderDialogService = strategyReminderDialogService;
        _timeProvider = timeProvider;

        var palette = _appThemeService.CurrentPalette;
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
    }

    public string[] GrabPollingModes { get; } = ["极限速度", "随机延迟", "延迟 5 秒"];

    public string[] GrabReservationStrategies { get; } = ["先获取列表判断状态", "直接发送预约请求"];

    [ObservableProperty]
    private bool isGrabSeatSelectionOverlayOpen;

    [ObservableProperty]
    private int selectedGrabPollingModeIndex = 2;

    [ObservableProperty]
    private int selectedGrabReservationStrategyIndex;

    [ObservableProperty]
    private bool isGrabScheduledStartEnabled;

    [ObservableProperty]
    private TimeSpan? scheduledStartTime = DefaultScheduledStartTime;

    [ObservableProperty]
    private string grabStatusText = "未运行";

    [ObservableProperty]
    private bool isGrabTaskActive;

    [ObservableProperty]
    private int grabPollCount;

    [ObservableProperty]
    private int grabRequestCount;

    [ObservableProperty]
    private string grabLastRequestText = "无";

    [ObservableProperty]
    private string grabRuntimeText = "00:00:00";

    public bool CanEditGrabConfiguration => !IsGrabTaskActive;

    public bool CanEditGrabScheduledStartTime => CanEditGrabConfiguration && IsGrabScheduledStartEnabled;

    public string GrabDashboardStatusText => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _grabStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush GrabDashboardStatusBrush => _grabTaskState switch
    {
        CoordinatorTaskState.Starting => _stateWarningBrush,
        CoordinatorTaskState.Running => _stateRunningBrush,
        CoordinatorTaskState.Stopping => _stateWarningBrush,
        CoordinatorTaskState.Completed when _grabStatusReason == CoordinatorStatusReason.Stopped => _stateFailureBrush,
        CoordinatorTaskState.Completed => _stateSuccessBrush,
        CoordinatorTaskState.Failed => _stateFailureBrush,
        _ => _stateIdleBrush
    };

    public TimeSpan? PendingScheduledStartDefault => _pendingGrabScheduledStartDefault;

    public void ConfigureOrchestration(
        Func<bool> isInitialized,
        Func<bool> isLoadingSettings,
        Func<bool> isOptimalStrategyReminderEnabled,
        Func<Task> flushPendingSystemSettingsAsync,
        Action<bool> applyPersistedOptimalStrategyReminder,
        Func<LibrarySummary?> selectedLibrary,
        Func<int> seatCount,
        Func<IReadOnlyList<SeatReference>> selectedSeats,
        Func<Task> refreshSeatsAsync,
        Action beginSeatSelectionDraft,
        Action commitSeatSelection,
        Action restoreCommittedSeatSelection,
        Func<Task> refreshSuccessReservationAsync,
        Func<Task> recordSuccessfulReservationAsync,
        Action<CoordinatorStatus> statusApplied)
    {
        _isInitialized = isInitialized;
        _isLoadingSettings = isLoadingSettings;
        _isOptimalStrategyReminderEnabled = isOptimalStrategyReminderEnabled;
        _flushPendingSystemSettingsAsync = flushPendingSystemSettingsAsync;
        _applyPersistedOptimalStrategyReminder = applyPersistedOptimalStrategyReminder;
        _selectedLibrary = selectedLibrary;
        _seatCount = seatCount;
        _selectedSeats = selectedSeats;
        _refreshSeatsAsync = refreshSeatsAsync;
        _beginSeatSelectionDraft = beginSeatSelectionDraft;
        _commitSeatSelection = commitSeatSelection;
        _restoreCommittedSeatSelection = restoreCommittedSeatSelection;
        _refreshSuccessReservationAsync = refreshSuccessReservationAsync;
        _recordSuccessfulReservationAsync = recordSuccessfulReservationAsync;
        _statusApplied = statusApplied;
    }

    public void InitializeStatus()
    {
        if (!_statusSubscribed)
        {
            _statusSubscribed = true;
            _grabSeatCoordinator.StatusChanged += OnGrabStatusChanged;
        }

        ApplyGrabStatus(_grabSeatCoordinator.GetStatus());
    }

    public void ApplySettings(AppSettings settings)
    {
        SelectedGrabReservationStrategyIndex = (int)settings.Tasks.Grab.ReservationStrategy;
        _grabScheduledStartDefault = NormalizeTimeOfDay(
            settings.Tasks.Grab.DefaultScheduledStartTime,
            DefaultScheduledStartTime);
        ScheduledStartTime = _grabScheduledStartDefault;
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
    }

    public GrabReservationStrategy CurrentReservationStrategy => (GrabReservationStrategy)Math.Clamp(
        SelectedGrabReservationStrategyIndex,
        0,
        GrabReservationStrategies.Length - 1);

    public void ApplyStatus(CoordinatorStatus status)
    {
        ApplyGrabStatus(status);
    }

    public void UpdateLastRequestText()
    {
        UpdateGrabLastRequestText();
    }

    public void UpdateRuntimeClock()
    {
        if (_grabRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGrabRuntimeText(GetCurrentTime());
    }

    public void CancelPendingScheduledStartDefaultAutoSave()
    {
        if (_grabScheduledStartDefaultAutoSaveCts is null)
        {
            return;
        }

        _grabScheduledStartDefaultAutoSaveCts.Cancel();
        _grabScheduledStartDefaultAutoSaveCts.Dispose();
        _grabScheduledStartDefaultAutoSaveCts = null;
    }

    public async Task FlushPendingScheduledStartDefaultAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingGrabScheduledStartDefault;
        CancelPendingScheduledStartDefaultAutoSave();
        if (pending is null)
        {
            return;
        }

        await PersistGrabScheduledStartDefaultAsync(pending.Value, cancellationToken);
        if (_pendingGrabScheduledStartDefault == pending)
        {
            _pendingGrabScheduledStartDefault = null;
        }
    }

    partial void OnIsGrabTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGrabConfiguration));
        OnPropertyChanged(nameof(CanEditGrabScheduledStartTime));
    }

    partial void OnIsGrabScheduledStartEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGrabScheduledStartTime));
    }

    partial void OnScheduledStartTimeChanged(TimeSpan? value)
    {
        if (value is null)
        {
            ScheduledStartTime = _grabScheduledStartDefault;
            return;
        }

        if (!IsTimeOfDay(value.Value))
        {
            return;
        }

        _grabScheduledStartDefault = value.Value;
        ScheduleGrabScheduledStartDefaultAutoSave(value.Value);
    }

    [RelayCommand]
    private async Task OpenGrabSeatSelectionOverlayAsync()
    {
        if (!CanEditGrabConfiguration)
        {
            return;
        }

        if (_selectedLibrary?.Invoke() is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再选择目标座位");
            return;
        }

        if (_seatCount?.Invoke() == 0 && _refreshSeatsAsync is not null)
        {
            await _refreshSeatsAsync();
        }

        if (_seatCount?.Invoke() == 0)
        {
            await _notificationService.ShowInfoAsync("暂无座位数据", "当前场馆还没有可供编辑的座位布局");
            return;
        }

        _beginSeatSelectionDraft?.Invoke();
        IsGrabSeatSelectionOverlayOpen = true;
    }

    [RelayCommand]
    private void ConfirmGrabSeatSelection()
    {
        _commitSeatSelection?.Invoke();
        IsGrabSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void CancelGrabSeatSelection()
    {
        _restoreCommittedSeatSelection?.Invoke();
        IsGrabSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private async Task StartGrabAsync()
    {
        var selectedLibrary = _selectedLibrary?.Invoke();
        if (selectedLibrary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆");
            return;
        }

        var selectedSeats = _selectedSeats?.Invoke().ToList() ?? [];
        if (selectedSeats.Count == 0)
        {
            await _notificationService.ShowWarningAsync("未选择座位", "请至少选中一个目标座位");
            return;
        }

        try
        {
            var mode = (GrabPollingMode)SelectedGrabPollingModeIndex;
            var scheduledStart = ParseScheduledTime();
            var reservationStrategy = CurrentReservationStrategy;
            var disableOptimalStrategyReminder = false;
            if (ShouldShowOptimalStrategyReminder(selectedSeats.Count, reservationStrategy))
            {
                var reminderResult = await _strategyReminderDialogService.ShowAsync();
                if (reminderResult.Decision == GrabStrategyReminderDecision.Cancel)
                {
                    return;
                }

                disableOptimalStrategyReminder = reminderResult.DisableReminder;

                if (reminderResult.Decision == GrabStrategyReminderDecision.SwitchToOptimal)
                {
                    reservationStrategy = GrabReservationStrategy.QueryThenReserve;
                }
            }

            await _flushPendingSystemSettingsAsync();
            await PersistGrabStartPreferencesAsync(
                reservationStrategy,
                disableOptimalStrategyReminder);
            SelectedGrabReservationStrategyIndex = (int)reservationStrategy;
            if (disableOptimalStrategyReminder)
            {
                _applyPersistedOptimalStrategyReminder(false);
            }

            var plan = new GrabSeatPlan(
                selectedLibrary.LibraryId,
                selectedLibrary.Name,
                selectedSeats,
                mode,
                GrabPollingStrategyFactory.FromMode(mode),
                scheduledStart);
            await _grabSeatCoordinator.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Grab", $"启动抢座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动抢座失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopGrabAsync()
    {
        try
        {
            await _grabSeatCoordinator.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Grab", $"停止抢座失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止抢座失败", ex.Message);
        }
    }

    private bool ShouldShowOptimalStrategyReminder(
        int selectedSeatCount,
        GrabReservationStrategy reservationStrategy)
    {
        return _isOptimalStrategyReminderEnabled() &&
               selectedSeatCount > OptimalStrategyReminderSeatThreshold &&
               reservationStrategy == GrabReservationStrategy.ReserveDirectly;
    }

    private async Task PersistGrabStartPreferencesAsync(
        GrabReservationStrategy strategy,
        bool disableOptimalStrategyReminder)
    {
        await _settingsWorkflowService.SaveGrabStartPreferencesAsync(
            strategy,
            disableOptimalStrategyReminder);
    }

    private TimeOnly? ParseScheduledTime()
    {
        if (!IsGrabScheduledStartEnabled)
        {
            return null;
        }

        var scheduledStart = ScheduledStartTime
            ?? throw new InvalidOperationException("抢座定时启动时间不能为空");

        return ToTimeOnly(scheduledStart, "抢座定时启动时间");
    }

    private void OnGrabStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyGrabStatus(status);
            TryRecordGrabSuccess(status);
        });

        if (status.State == CoordinatorTaskState.Completed &&
            status.Reason == CoordinatorStatusReason.GrabSucceeded)
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await RefreshGrabSuccessReservationAsync();
            });
        }
    }

    private async Task RefreshGrabSuccessReservationAsync()
    {
        if (_refreshSuccessReservationAsync is null)
        {
            return;
        }

        try
        {
            await _refreshSuccessReservationAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Grab", $"抢座成功后刷新预约状态失败：{ex.Message}");
        }
    }

    private void ApplyGrabStatus(CoordinatorStatus status)
    {
        GrabStatusText = status.Message;
        IsGrabTaskActive = IsTaskActive(status);
        GrabPollCount = status.PollCount;
        GrabRequestCount = status.RequestCount;
        _grabLastRequestAt = status.LastRequestAt;
        _grabTaskState = status.State;
        _grabStatusReason = status.Reason;
        UpdateGrabLastRequestText();
        ApplyGrabRuntime(status);
        _statusApplied?.Invoke(status);
        OnPropertyChanged(nameof(GrabDashboardStatusText));
        OnPropertyChanged(nameof(GrabDashboardStatusBrush));
    }

    private void UpdateGrabLastRequestText()
    {
        if (_grabLastRequestAt is null)
        {
            GrabLastRequestText = "无";
            return;
        }

        var elapsed = GetCurrentTime() - _grabLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        GrabLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private void ApplyGrabRuntime(CoordinatorStatus status)
    {
        switch (status.State)
        {
            case CoordinatorTaskState.Idle:
            case CoordinatorTaskState.Starting:
                ResetGrabRuntime();
                return;
            case CoordinatorTaskState.Running:
                _grabRuntimeStartedAt ??= status.LastUpdatedAt ?? GetCurrentTime();
                UpdateGrabRuntimeText(GetCurrentTime());
                return;
            case CoordinatorTaskState.Stopping:
            case CoordinatorTaskState.Completed:
            case CoordinatorTaskState.Failed:
                FreezeGrabRuntime(status.LastUpdatedAt);
                return;
        }
    }

    private void FreezeGrabRuntime(DateTimeOffset? stoppedAt)
    {
        if (_grabRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGrabRuntimeText(stoppedAt ?? GetCurrentTime());
        _grabRuntimeStartedAt = null;
    }

    private void ResetGrabRuntime()
    {
        _grabRuntimeStartedAt = null;
        GrabRuntimeText = "00:00:00";
    }

    private void UpdateGrabRuntimeText(DateTimeOffset timestamp)
    {
        if (_grabRuntimeStartedAt is null)
        {
            GrabRuntimeText = "00:00:00";
            return;
        }

        GrabRuntimeText = FormatElapsedClock(timestamp - _grabRuntimeStartedAt.Value);
    }

    private void TryRecordGrabSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.GrabSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? GetCurrentTime();
        if (_lastRecordedGrabSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedGrabSuccessAt = recordedAt;
        if (_recordSuccessfulReservationAsync is not null)
        {
            _ = _recordSuccessfulReservationAsync();
        }
    }

    private void ScheduleGrabScheduledStartDefaultAutoSave(TimeSpan value)
    {
        if (_isLoadingSettings?.Invoke() == true ||
            _isInitialized?.Invoke() != true ||
            !IsTimeOfDay(value))
        {
            return;
        }

        CancelPendingScheduledStartDefaultAutoSave();
        _pendingGrabScheduledStartDefault = value;
        var cts = new CancellationTokenSource();
        _grabScheduledStartDefaultAutoSaveCts = cts;
        _ = AutoSaveGrabScheduledStartDefaultAsync(value, cts, cts.Token);
    }

    private async Task AutoSaveGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationTokenSource cancellationTokenSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            await PersistGrabScheduledStartDefaultAsync(value, cancellationToken);
            ClearCompletedGrabScheduledStartDefaultAutoSave(cancellationTokenSource, value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存抢座定时时间默认值失败：{ex.Message}");
        }
    }

    private Task PersistGrabScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveGrabScheduledStartDefaultAsync(value, cancellationToken);
    }

    private void ClearCompletedGrabScheduledStartDefaultAutoSave(
        CancellationTokenSource cancellationTokenSource,
        TimeSpan value)
    {
        if (!ReferenceEquals(_grabScheduledStartDefaultAutoSaveCts, cancellationTokenSource))
        {
            return;
        }

        _grabScheduledStartDefaultAutoSaveCts.Dispose();
        _grabScheduledStartDefaultAutoSaveCts = null;
        if (_pendingGrabScheduledStartDefault == value)
        {
            _pendingGrabScheduledStartDefault = null;
        }
    }

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }

    private static TimeOnly ToTimeOnly(TimeSpan value, string fieldName)
    {
        if (!IsTimeOfDay(value))
        {
            throw new InvalidOperationException($"{fieldName}必须介于 00:00:00 和 23:59:59 之间");
        }

        return TimeOnly.FromTimeSpan(value);
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan value, TimeSpan fallback)
    {
        return IsTimeOfDay(value) ? value : fallback;
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private static bool IsTaskActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private static string FormatElapsedClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{Math.Max(0, (int)elapsed.TotalHours):D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
