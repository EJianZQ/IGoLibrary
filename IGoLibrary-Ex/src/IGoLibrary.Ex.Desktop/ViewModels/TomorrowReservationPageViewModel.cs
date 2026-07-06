using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class TomorrowReservationPageViewModel : ViewModelBase
{
    private static readonly TimeSpan DefaultScheduledStartTime =
        TomorrowReservationTaskSettings.Default.DefaultScheduledStartTime;

    private readonly ITomorrowReservationCoordinator _tomorrowReservationCoordinator;
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IAppThemeService _appThemeService;
    private readonly TimeProvider _timeProvider;
    private readonly ObservableCollection<SeatItemViewModel> _tomorrowSeats = [];

    private Func<bool>? _isInitialized;
    private Func<bool>? _isLoadingSettings;
    private Func<bool>? _hasActiveVenuePreview;
    private Func<LibrarySummary?>? _lockedLibrary;
    private Func<Task>? _refreshSeatsAsync;
    private Func<Task>? _recordSuccessfulReservationAsync;
    private Action<CoordinatorStatus>? _statusApplied;
    private bool _statusSubscribed;
    private CoordinatorTaskState _tomorrowTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _tomorrowStatusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _tomorrowLastRequestAt;
    private DateTimeOffset? _lastRecordedTomorrowSuccessAt;
    private TimeSpan _tomorrowScheduledStartDefault = DefaultScheduledStartTime;
    private TimeSpan? _pendingTomorrowScheduledStartDefault;
    private CancellationTokenSource? _tomorrowScheduledStartDefaultAutoSaveCts;
    private bool _isSynchronizingTomorrowSeatSelection;
    private string? _draftTomorrowSeatKey;
    private string? _pendingStartVerificationText;
    private IBrush _stateIdleBrush;
    private IBrush _stateRunningBrush;
    private IBrush _stateSuccessBrush;
    private IBrush _stateWarningBrush;
    private IBrush _stateFailureBrush;

    public TomorrowReservationPageViewModel(
        ITomorrowReservationCoordinator tomorrowReservationCoordinator,
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService,
        TimeProvider timeProvider)
    {
        _tomorrowReservationCoordinator = tomorrowReservationCoordinator;
        _settingsWorkflowService = settingsWorkflowService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _appThemeService = appThemeService;
        _timeProvider = timeProvider;

        var palette = _appThemeService.CurrentPalette;
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
    }

    public ObservableCollection<SeatItemViewModel> TomorrowVisibleSeats { get; } = [];

    [ObservableProperty]
    private bool isTomorrowSeatSelectionOverlayOpen;

    [ObservableProperty]
    private TimeSpan? tomorrowScheduledStartTime = DefaultScheduledStartTime;

    [ObservableProperty]
    private string tomorrowStatusText = "未运行";

    [ObservableProperty]
    private bool isTomorrowTaskActive;

    [ObservableProperty]
    private int tomorrowRequestCount;

    [ObservableProperty]
    private string tomorrowLastRequestText = "无";

    [ObservableProperty]
    private string tomorrowVerificationText = "尚未执行明日预约";

    [ObservableProperty]
    private string tomorrowSeatFilterText = string.Empty;

    [ObservableProperty]
    private SeatReference? selectedTomorrowSeat;

    public bool CanEditTomorrowConfiguration => !IsTomorrowTaskActive && _hasActiveVenuePreview?.Invoke() != true;

    public bool HasSelectedTomorrowSeat => SelectedTomorrowSeat is not null;

    public bool HasNoSelectedTomorrowSeat => !HasSelectedTomorrowSeat;

    public bool HasTomorrowSeatLayout => _tomorrowSeats.Count > 0;

    public bool HasNoTomorrowSeatLayout => !HasTomorrowSeatLayout;

    public bool HasVisibleTomorrowSeatResults => _tomorrowSeats.Any(static seat => seat.IsFilterVisible);

    public bool ShowTomorrowSeatFilterEmptyState => HasTomorrowSeatLayout && !HasVisibleTomorrowSeatResults;

    public string SelectedTomorrowSeatText => SelectedTomorrowSeat is null
        ? "尚未选择明日预约座位"
        : $"已选择 {SelectedTomorrowSeat.SeatName}";

    public string DraftSelectedTomorrowSeatSummaryText
    {
        get
        {
            var seat = _tomorrowSeats.FirstOrDefault(x =>
                string.Equals(x.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal));
            return seat is null
                ? "本次尚未选择明日预约座位"
                : $"本次已选择 {seat.SeatName}";
        }
    }

    public string TomorrowDashboardStatusText => _tomorrowTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _tomorrowStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush TomorrowDashboardStatusBrush => _tomorrowTaskState switch
    {
        CoordinatorTaskState.Starting => _stateWarningBrush,
        CoordinatorTaskState.Running => _stateRunningBrush,
        CoordinatorTaskState.Stopping => _stateWarningBrush,
        CoordinatorTaskState.Completed when _tomorrowStatusReason == CoordinatorStatusReason.Stopped => _stateFailureBrush,
        CoordinatorTaskState.Completed => _stateSuccessBrush,
        CoordinatorTaskState.Failed => _stateFailureBrush,
        _ => _stateIdleBrush
    };

    public TimeSpan? PendingScheduledStartDefault => _pendingTomorrowScheduledStartDefault;

    public void ConfigureOrchestration(
        Func<bool> isInitialized,
        Func<bool> isLoadingSettings,
        Func<bool> hasActiveVenuePreview,
        Func<LibrarySummary?> lockedLibrary,
        Func<Task> refreshSeatsAsync,
        Func<Task> recordSuccessfulReservationAsync,
        Action<CoordinatorStatus> statusApplied)
    {
        _isInitialized = isInitialized;
        _isLoadingSettings = isLoadingSettings;
        _hasActiveVenuePreview = hasActiveVenuePreview;
        _lockedLibrary = lockedLibrary;
        _refreshSeatsAsync = refreshSeatsAsync;
        _recordSuccessfulReservationAsync = recordSuccessfulReservationAsync;
        _statusApplied = statusApplied;
    }

    public void InitializeStatus()
    {
        if (!_statusSubscribed)
        {
            _statusSubscribed = true;
            _tomorrowReservationCoordinator.StatusChanged += OnTomorrowStatusChanged;
        }

        ApplyTomorrowStatus(_tomorrowReservationCoordinator.GetStatus());
    }

    public void ApplySettings(AppSettings settings)
    {
        _tomorrowScheduledStartDefault = NormalizeTimeOfDay(
            settings.Tasks.TomorrowReservation.DefaultScheduledStartTime,
            DefaultScheduledStartTime);
        TomorrowScheduledStartTime = _tomorrowScheduledStartDefault;
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        OnPropertyChanged(nameof(TomorrowDashboardStatusBrush));
    }

    public void NotifyVenuePreviewChanged()
    {
        OnPropertyChanged(nameof(CanEditTomorrowConfiguration));
    }

    public void PopulateSeats(LibraryLayout layout)
    {
        foreach (var seat in _tomorrowSeats)
        {
            seat.PropertyChanged -= OnTomorrowSeatItemPropertyChanged;
        }

        _tomorrowSeats.Clear();
        TomorrowVisibleSeats.Clear();

        var selectedKey = IsTomorrowSeatSelectionOverlayOpen
            ? _draftTomorrowSeatKey
            : SelectedTomorrowSeat?.SeatKey;
        _isSynchronizingTomorrowSeatSelection = true;
        try
        {
            foreach (var seat in layout.Seats.Where(seat => !string.IsNullOrWhiteSpace(seat.SeatName)))
            {
                var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied)
                {
                    IsSelected = string.Equals(seat.SeatKey, selectedKey, StringComparison.Ordinal)
                };
                item.PropertyChanged += OnTomorrowSeatItemPropertyChanged;
                _tomorrowSeats.Add(item);
                TomorrowVisibleSeats.Add(item);
            }
        }
        finally
        {
            _isSynchronizingTomorrowSeatSelection = false;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            if (!string.IsNullOrWhiteSpace(_draftTomorrowSeatKey) &&
                _tomorrowSeats.All(seat => !string.Equals(seat.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal)))
            {
                _draftTomorrowSeatKey = null;
            }
        }
        else if (SelectedTomorrowSeat is not null &&
            _tomorrowSeats.All(seat => !string.Equals(seat.SeatKey, SelectedTomorrowSeat.SeatKey, StringComparison.Ordinal)))
        {
            SelectedTomorrowSeat = null;
        }

        ApplyTomorrowSeatFilter();
        OnPropertyChanged(nameof(HasTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasNoTomorrowSeatLayout));
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    public void ClearSeats()
    {
        foreach (var seat in _tomorrowSeats)
        {
            seat.PropertyChanged -= OnTomorrowSeatItemPropertyChanged;
        }

        _tomorrowSeats.Clear();
        TomorrowVisibleSeats.Clear();
        _draftTomorrowSeatKey = null;
        SelectedTomorrowSeat = null;
        OnPropertyChanged(nameof(HasTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasNoTomorrowSeatLayout));
        OnPropertyChanged(nameof(HasVisibleTomorrowSeatResults));
        OnPropertyChanged(nameof(ShowTomorrowSeatFilterEmptyState));
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    public void ApplyStatus(CoordinatorStatus status)
    {
        ApplyTomorrowStatus(status);
    }

    public void UpdateLastRequestText()
    {
        UpdateTomorrowLastRequestText();
    }

    public void CancelPendingScheduledStartDefaultAutoSave()
    {
        if (_tomorrowScheduledStartDefaultAutoSaveCts is null)
        {
            return;
        }

        _tomorrowScheduledStartDefaultAutoSaveCts.Cancel();
        _tomorrowScheduledStartDefaultAutoSaveCts.Dispose();
        _tomorrowScheduledStartDefaultAutoSaveCts = null;
    }

    public async Task FlushPendingScheduledStartDefaultAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingTomorrowScheduledStartDefault;
        CancelPendingScheduledStartDefaultAutoSave();
        if (pending is null)
        {
            return;
        }

        await PersistTomorrowScheduledStartDefaultAsync(pending.Value, cancellationToken);
        if (_pendingTomorrowScheduledStartDefault == pending)
        {
            _pendingTomorrowScheduledStartDefault = null;
        }
    }

    partial void OnTomorrowScheduledStartTimeChanged(TimeSpan? value)
    {
        if (value is null)
        {
            TomorrowScheduledStartTime = _tomorrowScheduledStartDefault;
            return;
        }

        if (!IsTimeOfDay(value.Value))
        {
            return;
        }

        _tomorrowScheduledStartDefault = value.Value;
        ScheduleTomorrowScheduledStartDefaultAutoSave(value.Value);
    }

    partial void OnIsTomorrowTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditTomorrowConfiguration));
        if (!value)
        {
            return;
        }

        RestoreCommittedTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    partial void OnSelectedTomorrowSeatChanged(SeatReference? value)
    {
        OnPropertyChanged(nameof(HasSelectedTomorrowSeat));
        OnPropertyChanged(nameof(HasNoSelectedTomorrowSeat));
        OnPropertyChanged(nameof(SelectedTomorrowSeatText));
    }

    partial void OnTomorrowSeatFilterTextChanged(string value) => ApplyTomorrowSeatFilter();

    [RelayCommand]
    private async Task RefreshTomorrowSeatsAsync()
    {
        if (_refreshSeatsAsync is not null)
        {
            await _refreshSeatsAsync();
        }
    }

    [RelayCommand]
    private async Task OpenTomorrowSeatSelectionOverlayAsync()
    {
        if (IsTomorrowTaskActive)
        {
            return;
        }

        if (_hasActiveVenuePreview?.Invoke() == true)
        {
            await _notificationService.ShowWarningAsync("正在预览场馆", "请先锁定当前预览场馆后再进行明日预约");
            return;
        }

        if (_lockedLibrary?.Invoke() is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再选择明日预约目标座位");
            return;
        }

        if (_tomorrowSeats.Count == 0 && _refreshSeatsAsync is not null)
        {
            await _refreshSeatsAsync();
        }

        if (_tomorrowSeats.Count == 0)
        {
            await _notificationService.ShowInfoAsync("暂无座位数据", "当前场馆还没有可供选择的座位布局");
            return;
        }

        BeginTomorrowSeatSelectionDraft();
        IsTomorrowSeatSelectionOverlayOpen = true;
    }

    [RelayCommand]
    private void ConfirmTomorrowSeatSelection()
    {
        CommitTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void CancelTomorrowSeatSelection()
    {
        RestoreCommittedTomorrowSeatSelection();
        IsTomorrowSeatSelectionOverlayOpen = false;
    }

    [RelayCommand]
    private void ClearTomorrowSeat()
    {
        if (!CanEditTomorrowConfiguration)
        {
            return;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            _draftTomorrowSeatKey = null;
            ApplyTomorrowSeatSelection(null);
            UpdateDraftTomorrowSeatSelectionPresentation();
            return;
        }

        SelectedTomorrowSeat = null;
        ApplyTomorrowSeatSelection(null);
    }

    [RelayCommand]
    private async Task StartTomorrowReservationAsync()
    {
        await StartTomorrowReservationCoreAsync(executeImmediately: false);
    }

    [RelayCommand]
    private async Task RunTomorrowReservationNowAsync()
    {
        await StartTomorrowReservationCoreAsync(executeImmediately: true);
    }

    [RelayCommand]
    private async Task StopTomorrowReservationAsync()
    {
        try
        {
            await _tomorrowReservationCoordinator.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Tomorrow", $"停止明日预约失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止明日预约失败", ex.Message);
        }
    }

    private async Task StartTomorrowReservationCoreAsync(bool executeImmediately)
    {
        if (IsTomorrowTaskActive)
        {
            return;
        }

        if (_hasActiveVenuePreview?.Invoke() == true)
        {
            await _notificationService.ShowWarningAsync("正在预览场馆", "请先锁定当前预览场馆后再进行明日预约");
            return;
        }

        var lockedLibrary = _lockedLibrary?.Invoke();
        if (lockedLibrary is null)
        {
            await _notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆");
            return;
        }

        var selectedSeat = SelectedTomorrowSeat;
        if (selectedSeat is null)
        {
            await _notificationService.ShowWarningAsync("未选择座位", "请先选择一个明日预约目标座位");
            return;
        }

        var selectedSeatInLayout = _tomorrowSeats.FirstOrDefault(seat =>
            string.Equals(seat.SeatKey, selectedSeat.SeatKey, StringComparison.Ordinal));
        if (selectedSeatInLayout is null)
        {
            SelectedTomorrowSeat = null;
            ApplyTomorrowSeatSelection(null);
            await _notificationService.ShowWarningAsync("座位已失效", "请重新选择明日预约目标座位");
            return;
        }

        try
        {
            var scheduledStart = ParseTomorrowScheduledTime();
            var verificationText = executeImmediately
                ? "明日预约任务已启动，等待结果"
                : $"等待触发：{scheduledStart:HH\\:mm\\:ss}";
            _pendingStartVerificationText = verificationText;
            TomorrowVerificationText = verificationText;
            var plan = new TomorrowReservationPlan(
                lockedLibrary.LibraryId,
                lockedLibrary.Name,
                new SeatReference(selectedSeatInLayout.SeatKey, selectedSeatInLayout.SeatName),
                scheduledStart,
                executeImmediately);
            await _tomorrowReservationCoordinator.StartAsync(plan);
            TomorrowVerificationText = verificationText;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "Tomorrow", $"启动明日预约失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("启动明日预约失败", ex.Message);
        }
    }

    private void OnTomorrowSeatItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingTomorrowSeatSelection ||
            e.PropertyName != nameof(SeatItemViewModel.IsSelected) ||
            sender is not SeatItemViewModel changedSeat)
        {
            return;
        }

        if (!changedSeat.IsSelected)
        {
            if (IsTomorrowSeatSelectionOverlayOpen)
            {
                if (string.Equals(_draftTomorrowSeatKey, changedSeat.SeatKey, StringComparison.Ordinal))
                {
                    _draftTomorrowSeatKey = null;
                    UpdateDraftTomorrowSeatSelectionPresentation();
                }

                return;
            }

            if (SelectedTomorrowSeat?.SeatKey == changedSeat.SeatKey)
            {
                SelectedTomorrowSeat = null;
            }

            return;
        }

        if (IsTomorrowSeatSelectionOverlayOpen)
        {
            _draftTomorrowSeatKey = changedSeat.SeatKey;
            ApplyTomorrowSeatSelection(changedSeat.SeatKey);
            UpdateDraftTomorrowSeatSelectionPresentation();
            return;
        }

        SelectedTomorrowSeat = new SeatReference(changedSeat.SeatKey, changedSeat.SeatName);
        ApplyTomorrowSeatSelection(changedSeat.SeatKey);
    }

    private void BeginTomorrowSeatSelectionDraft()
    {
        _draftTomorrowSeatKey = SelectedTomorrowSeat?.SeatKey;
        ApplyTomorrowSeatSelection(_draftTomorrowSeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void CommitTomorrowSeatSelection()
    {
        var selectedSeat = _tomorrowSeats.FirstOrDefault(x =>
            string.Equals(x.SeatKey, _draftTomorrowSeatKey, StringComparison.Ordinal));
        SelectedTomorrowSeat = selectedSeat is null
            ? null
            : new SeatReference(selectedSeat.SeatKey, selectedSeat.SeatName);
        _draftTomorrowSeatKey = null;
        ApplyTomorrowSeatSelection(SelectedTomorrowSeat?.SeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void RestoreCommittedTomorrowSeatSelection()
    {
        _draftTomorrowSeatKey = null;
        ApplyTomorrowSeatSelection(SelectedTomorrowSeat?.SeatKey);
        UpdateDraftTomorrowSeatSelectionPresentation();
    }

    private void ApplyTomorrowSeatSelection(string? selectedSeatKey)
    {
        _isSynchronizingTomorrowSeatSelection = true;
        try
        {
            foreach (var seat in _tomorrowSeats)
            {
                seat.IsSelected = !string.IsNullOrWhiteSpace(selectedSeatKey) &&
                                  string.Equals(seat.SeatKey, selectedSeatKey, StringComparison.Ordinal);
            }
        }
        finally
        {
            _isSynchronizingTomorrowSeatSelection = false;
        }
    }

    private void ApplyTomorrowSeatFilter()
    {
        var filterText = TomorrowSeatFilterText;
        foreach (var seat in _tomorrowSeats)
        {
            seat.IsFilterVisible = string.IsNullOrWhiteSpace(filterText) ||
                                   seat.SeatName.Contains(filterText, StringComparison.OrdinalIgnoreCase);
        }

        OnPropertyChanged(nameof(HasVisibleTomorrowSeatResults));
        OnPropertyChanged(nameof(ShowTomorrowSeatFilterEmptyState));
    }

    private void UpdateDraftTomorrowSeatSelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftSelectedTomorrowSeatSummaryText));
    }

    private TimeOnly ParseTomorrowScheduledTime()
    {
        var scheduledStart = TomorrowScheduledStartTime
            ?? throw new InvalidOperationException("明日预约触发时间不能为空");

        return ToTimeOnly(scheduledStart, "明日预约触发时间");
    }

    private void OnTomorrowStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTomorrowStatus(status);
            TryRecordTomorrowSuccess(status);
        });
    }

    private void ApplyTomorrowStatus(CoordinatorStatus status)
    {
        var message = TrimSentenceEnding(status.Message);
        TomorrowStatusText = message;
        IsTomorrowTaskActive = IsTaskActive(status);
        TomorrowRequestCount = status.RequestCount;
        _tomorrowLastRequestAt = status.LastRequestAt;
        _tomorrowTaskState = status.State;
        _tomorrowStatusReason = status.Reason;
        TomorrowVerificationText = status.State switch
        {
            CoordinatorTaskState.Idle => ClearPendingStartVerificationText("尚未执行明日预约"),
            CoordinatorTaskState.Starting or
                CoordinatorTaskState.Running when _pendingStartVerificationText is not null => _pendingStartVerificationText,
            CoordinatorTaskState.Running or
                CoordinatorTaskState.Stopping or
                CoordinatorTaskState.Completed or
                CoordinatorTaskState.Failed => ClearPendingStartVerificationText(message),
            _ => TomorrowVerificationText
        };

        UpdateTomorrowLastRequestText();
        _statusApplied?.Invoke(status);
        OnPropertyChanged(nameof(TomorrowDashboardStatusText));
        OnPropertyChanged(nameof(TomorrowDashboardStatusBrush));
    }

    private void UpdateTomorrowLastRequestText()
    {
        if (_tomorrowLastRequestAt is null)
        {
            TomorrowLastRequestText = "无";
            return;
        }

        var elapsed = GetCurrentTime() - _tomorrowLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        TomorrowLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private string ClearPendingStartVerificationText(string value)
    {
        _pendingStartVerificationText = null;
        return value;
    }

    private void TryRecordTomorrowSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.TomorrowReservationSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? GetCurrentTime();
        if (_lastRecordedTomorrowSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedTomorrowSuccessAt = recordedAt;
        if (_recordSuccessfulReservationAsync is not null)
        {
            _ = _recordSuccessfulReservationAsync();
        }
    }

    private void ScheduleTomorrowScheduledStartDefaultAutoSave(TimeSpan value)
    {
        if (_isLoadingSettings?.Invoke() == true ||
            _isInitialized?.Invoke() != true ||
            !IsTimeOfDay(value))
        {
            return;
        }

        CancelPendingScheduledStartDefaultAutoSave();
        _pendingTomorrowScheduledStartDefault = value;
        var cts = new CancellationTokenSource();
        _tomorrowScheduledStartDefaultAutoSaveCts = cts;
        _ = AutoSaveTomorrowScheduledStartDefaultAsync(value, cts, cts.Token);
    }

    private async Task AutoSaveTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationTokenSource cancellationTokenSource,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken);
            await PersistTomorrowScheduledStartDefaultAsync(value, cancellationToken);
            ClearCompletedTomorrowScheduledStartDefaultAutoSave(cancellationTokenSource, value);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存明日预约触发时间默认值失败：{ex.Message}");
        }
    }

    private Task PersistTomorrowScheduledStartDefaultAsync(
        TimeSpan value,
        CancellationToken cancellationToken = default)
    {
        return _settingsWorkflowService.SaveTomorrowScheduledStartDefaultAsync(value, cancellationToken);
    }

    private void ClearCompletedTomorrowScheduledStartDefaultAutoSave(
        CancellationTokenSource cancellationTokenSource,
        TimeSpan value)
    {
        if (!ReferenceEquals(_tomorrowScheduledStartDefaultAutoSaveCts, cancellationTokenSource))
        {
            return;
        }

        _tomorrowScheduledStartDefaultAutoSaveCts.Dispose();
        _tomorrowScheduledStartDefaultAutoSaveCts = null;
        if (_pendingTomorrowScheduledStartDefault == value)
        {
            _pendingTomorrowScheduledStartDefault = null;
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

    private static string TrimSentenceEnding(string message)
    {
        return string.IsNullOrEmpty(message)
            ? message
            : message.TrimEnd('。', '.');
    }
}
