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

public sealed partial class GlobalLeakPageViewModel : ViewModelBase
{
    private readonly IGlobalLeakCoordinator _globalLeakCoordinator;
    private readonly ITaskLaunchService _taskLaunchService;
    private readonly IVenueWorkflowService _venueWorkflowService;
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IAppThemeService _appThemeService;
    private readonly TimeProvider _timeProvider;
    private readonly GlobalLeakLibrarySelectionViewModel _librarySelection;

    private Func<bool>? _isAuthorized;
    private Func<Task>? _refreshSuccessReservationAsync;
    private Func<Task>? _recordSuccessfulReservationAsync;
    private Action<CoordinatorStatus>? _statusApplied;
    private bool _globalLeakSelectionRestoredForCurrentSession;
    private bool _statusSubscribed;
    private CoordinatorTaskState _globalLeakTaskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _globalLeakStatusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _globalLeakLastRequestAt;
    private DateTimeOffset? _globalLeakRuntimeStartedAt;
    private DateTimeOffset? _lastRecordedGlobalLeakSuccessAt;
    private IBrush _stateIdleBrush;
    private IBrush _stateRunningBrush;
    private IBrush _stateSuccessBrush;
    private IBrush _stateWarningBrush;
    private IBrush _stateFailureBrush;

    public GlobalLeakPageViewModel(
        IGlobalLeakCoordinator globalLeakCoordinator,
        ITaskLaunchService taskLaunchService,
        IVenueWorkflowService venueWorkflowService,
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService,
        TimeProvider timeProvider,
        GlobalLeakLibrarySelectionViewModel librarySelection)
    {
        _globalLeakCoordinator = globalLeakCoordinator;
        _taskLaunchService = taskLaunchService;
        _venueWorkflowService = venueWorkflowService;
        _settingsWorkflowService = settingsWorkflowService;
        _activityLogService = activityLogService;
        _notificationService = notificationService;
        _appThemeService = appThemeService;
        _timeProvider = timeProvider;
        _librarySelection = librarySelection;
        _librarySelection.PropertyChanged += OnLibrarySelectionPropertyChanged;

        var palette = _appThemeService.CurrentPalette;
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
    }

    public ObservableCollection<GlobalLeakLibraryItemViewModel> GlobalLeakLibraries => _librarySelection.Libraries;

    public ObservableCollection<GlobalLeakLibraryTarget> SelectedGlobalLeakLibraries => _librarySelection.SelectedLibraries;

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> SelectedGlobalLeakLibraryPriorities =>
        _librarySelection.SelectedPriorities;

    public ObservableCollection<GlobalLeakLibraryPriorityItemViewModel> DraftGlobalLeakLibraryPriorities =>
        _librarySelection.DraftPriorities;

    [ObservableProperty]
    private bool isGlobalLeakLibraryPickerOpen;

    [ObservableProperty]
    private string globalLeakStatusText = "未运行";

    [ObservableProperty]
    private bool isGlobalLeakTaskActive;

    [ObservableProperty]
    private bool isGlobalLeakSelectionSaving;

    [ObservableProperty]
    private int globalLeakScanRoundCount;

    [ObservableProperty]
    private int globalLeakRequestCount;

    [ObservableProperty]
    private string globalLeakLastRequestText = "无";

    [ObservableProperty]
    private string globalLeakRuntimeText = "00:00:00";

    [ObservableProperty]
    private int globalLeakScanIntervalSeconds = 10;

    public bool HasGlobalLeakLibraries => _librarySelection.HasLibraries;

    public bool HasNoGlobalLeakLibraries => _librarySelection.HasNoLibraries;

    public int SelectedGlobalLeakLibraryCount => _librarySelection.SelectedCount;

    public bool HasSelectedGlobalLeakLibraries => _librarySelection.HasSelectedLibraries;

    public bool HasNoSelectedGlobalLeakLibraries => _librarySelection.HasNoSelectedLibraries;

    public bool HasDraftGlobalLeakLibraries => _librarySelection.HasDraftLibraries;

    public bool HasNoDraftGlobalLeakLibraries => _librarySelection.HasNoDraftLibraries;

    public bool CanEditGlobalLeakConfiguration => !IsGlobalLeakTaskActive && !IsGlobalLeakSelectionSaving;

    public bool CanCancelGlobalLeakLibraryPicker => !IsGlobalLeakSelectionSaving;

    public string SelectedGlobalLeakLibrarySummaryText => _librarySelection.SelectedSummaryText;

    public int DraftGlobalLeakLibraryCount => _librarySelection.DraftCount;

    public string DraftGlobalLeakLibrarySummaryText => _librarySelection.DraftSummaryText;

    public string GlobalLeakDashboardStatusText => _globalLeakTaskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _globalLeakStatusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush GlobalLeakDashboardStatusBrush => _globalLeakTaskState switch
    {
        CoordinatorTaskState.Starting => _stateWarningBrush,
        CoordinatorTaskState.Running => _stateRunningBrush,
        CoordinatorTaskState.Stopping => _stateWarningBrush,
        CoordinatorTaskState.Completed when _globalLeakStatusReason == CoordinatorStatusReason.Stopped => _stateFailureBrush,
        CoordinatorTaskState.Completed => _stateSuccessBrush,
        CoordinatorTaskState.Failed => _stateFailureBrush,
        _ => _stateIdleBrush
    };

    public void ConfigureOrchestration(
        Func<bool> isAuthorized,
        Func<Task> refreshSuccessReservationAsync,
        Func<Task> recordSuccessfulReservationAsync,
        Action<CoordinatorStatus> statusApplied)
    {
        _isAuthorized = isAuthorized;
        _refreshSuccessReservationAsync = refreshSuccessReservationAsync;
        _recordSuccessfulReservationAsync = recordSuccessfulReservationAsync;
        _statusApplied = statusApplied;
    }

    public void InitializeStatus()
    {
        if (!_statusSubscribed)
        {
            _statusSubscribed = true;
            _globalLeakCoordinator.StatusChanged += OnGlobalLeakStatusChanged;
        }

        ApplyGlobalLeakStatus(_globalLeakCoordinator.GetStatus());
    }

    public void ApplyThemePalette(AppThemePalette palette)
    {
        _stateIdleBrush = palette.IdleBrush;
        _stateRunningBrush = palette.RunningBrush;
        _stateSuccessBrush = palette.SuccessBrush;
        _stateWarningBrush = palette.WarningBrush;
        _stateFailureBrush = palette.FailureBrush;
        OnPropertyChanged(nameof(GlobalLeakDashboardStatusBrush));
    }

    public void PopulateLibraries(IEnumerable<LibrarySummary> libraries)
    {
        _librarySelection.PopulateLibraries(libraries);
    }

    public void ClearLibraries()
    {
        _librarySelection.ClearLibraries();
    }

    public void ResetRestoredSelectionForCurrentSession()
    {
        _globalLeakSelectionRestoredForCurrentSession = false;
    }

    public async Task RestoreLibrarySelectionAsync()
    {
        if (_globalLeakSelectionRestoredForCurrentSession)
        {
            return;
        }

        try
        {
            var settings = await _settingsWorkflowService.LoadAsync();
            var storedLibraries = settings.Tasks.GlobalLeak.SelectedLibraries;
            var restoreResult = _librarySelection.RestoreCommittedLibraries(
                storedLibraries.Select(static library => new GlobalLeakLibraryTarget(
                    library.LibraryId,
                    library.LibraryName,
                    library.Floor)));

            if (restoreResult.RestoredCount > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"已按优先级恢复 {restoreResult.RestoredCount} 个全域捡漏扫描场馆。");
            }

            if (restoreResult.SkippedCount > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"有 {restoreResult.SkippedCount} 个历史全域捡漏场馆不在当前账号场馆列表中，已跳过。");
            }

            _globalLeakSelectionRestoredForCurrentSession = true;
        }
        catch (Exception ex)
        {
            _globalLeakSelectionRestoredForCurrentSession = false;
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"恢复全域捡漏扫描场馆失败：{ex.Message}", ex);
        }
    }

    public void ApplyStatus(CoordinatorStatus status)
    {
        ApplyGlobalLeakStatus(status);
    }

    public void UpdateRuntimeClock()
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGlobalLeakRuntimeText(GetCurrentTime());
    }

    public void UpdateLastRequestText()
    {
        UpdateGlobalLeakLastRequestText();
    }

    partial void OnIsGlobalLeakTaskActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGlobalLeakConfiguration));
    }

    partial void OnIsGlobalLeakSelectionSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGlobalLeakConfiguration));
        OnPropertyChanged(nameof(CanCancelGlobalLeakLibraryPicker));
    }

    partial void OnGlobalLeakScanIntervalSecondsChanged(int value)
    {
        var normalized = Math.Clamp(value, 1, 3600);
        if (normalized != value)
        {
            GlobalLeakScanIntervalSeconds = normalized;
        }
    }

    [RelayCommand]
    private async Task OpenGlobalLeakLibraryPickerAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (_isAuthorized?.Invoke() != true)
        {
            await _notificationService.ShowWarningAsync("未登录", "请先授权后再选择扫描场馆");
            return;
        }

        await LoadLibrariesForSelectionAsync();

        if (GlobalLeakLibraries.Count == 0)
        {
            await _notificationService.ShowInfoAsync("暂无场馆数据", "当前账号还没有可用场馆列表");
            return;
        }

        _librarySelection.BeginDraft();
        IsGlobalLeakLibraryPickerOpen = true;
    }

    [RelayCommand]
    private async Task RefreshGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        await LoadLibrariesForSelectionAsync();
    }

    [RelayCommand]
    private async Task ConfirmGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        var selectedLibraries = _librarySelection.GetDraftSnapshot();
        IsGlobalLeakSelectionSaving = true;
        try
        {
            if (!await TryPersistGlobalLeakLibrarySelectionAsync(selectedLibraries))
            {
                return;
            }

            _librarySelection.SetCommittedLibraries(selectedLibraries);
            IsGlobalLeakLibraryPickerOpen = false;
        }
        finally
        {
            IsGlobalLeakSelectionSaving = false;
        }
    }

    [RelayCommand]
    private void CancelGlobalLeakLibraries()
    {
        if (!CanCancelGlobalLeakLibraryPicker)
        {
            return;
        }

        _librarySelection.CancelDraft();
        IsGlobalLeakLibraryPickerOpen = false;
    }

    [RelayCommand]
    private async Task SelectAllGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!IsGlobalLeakLibraryPickerOpen)
        {
            var selectedLibraries = GlobalLeakLibraries
                .Select(static library => new GlobalLeakLibraryTarget(
                    library.LibraryId,
                    library.LibraryName,
                    library.Floor))
                .ToArray();
            if (!await TryPersistGlobalLeakLibrarySelectionAsync(selectedLibraries))
            {
                return;
            }

            _librarySelection.SetCommittedLibraries(selectedLibraries);
            return;
        }

        _librarySelection.SelectAllDraft();
    }

    [RelayCommand]
    private async Task ClearGlobalLeakLibrarySelectionAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!await TryPersistGlobalLeakLibrarySelectionAsync(Array.Empty<GlobalLeakLibraryTarget>()))
        {
            return;
        }

        _librarySelection.SetCommittedLibraries([]);
    }

    [RelayCommand]
    private async Task ClearDraftGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!IsGlobalLeakLibraryPickerOpen)
        {
            await ClearGlobalLeakLibrarySelectionAsync();
            return;
        }

        _librarySelection.ClearDraft();
    }

    [RelayCommand]
    private async Task RemoveSelectedGlobalLeakLibraryAsync(GlobalLeakLibraryTarget? target)
    {
        if (target is null || !CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!SelectedGlobalLeakLibraries.Any(library => library.LibraryId == target.LibraryId))
        {
            return;
        }

        var nextLibraries = _librarySelection.CreateSelectedSnapshotWithout(target.LibraryId);
        if (!await TryPersistGlobalLeakLibrarySelectionAsync(nextLibraries))
        {
            return;
        }

        _librarySelection.SetCommittedLibraries(nextLibraries);
    }

    [RelayCommand]
    private void MoveGlobalLeakLibraryUp(GlobalLeakLibraryPriorityItemViewModel? item)
    {
        if (item is null || !CanEditGlobalLeakConfiguration)
        {
            return;
        }

        _librarySelection.MoveDraftLibraryByOffset(item.LibraryId, -1);
    }

    [RelayCommand]
    private void MoveGlobalLeakLibraryDown(GlobalLeakLibraryPriorityItemViewModel? item)
    {
        if (item is null || !CanEditGlobalLeakConfiguration)
        {
            return;
        }

        _librarySelection.MoveDraftLibraryByOffset(item.LibraryId, 1);
    }

    public bool MoveDraftGlobalLeakLibrary(int sourceLibraryId, int targetLibraryId, bool insertAfter)
    {
        return CanEditGlobalLeakConfiguration &&
               IsGlobalLeakLibraryPickerOpen &&
               _librarySelection.MoveDraftLibrary(sourceLibraryId, targetLibraryId, insertAfter);
    }

    public bool SetGlobalLeakLibraryDropIndicator(int targetLibraryId, bool insertAfter)
    {
        return CanEditGlobalLeakConfiguration &&
               IsGlobalLeakLibraryPickerOpen &&
               _librarySelection.SetDropIndicator(targetLibraryId, insertAfter);
    }

    public void ClearGlobalLeakLibraryDropIndicators()
    {
        _librarySelection.ClearDropIndicators();
    }

    [RelayCommand]
    private async Task StartGlobalLeakAsync()
    {
        if (IsGlobalLeakTaskActive)
        {
            return;
        }

        var selectedLibraries = _librarySelection.GetSelectedSnapshot();
        if (selectedLibraries.Length == 0)
        {
            await _notificationService.ShowWarningAsync("未选择场馆", "请至少选择一个全域捡漏扫描场馆");
            return;
        }

        try
        {
            var intervalSeconds = Math.Clamp(GlobalLeakScanIntervalSeconds, 1, 3600);
            GlobalLeakScanIntervalSeconds = intervalSeconds;
            var plan = new GlobalLeakPlan(
                selectedLibraries,
                TimeSpan.FromSeconds(intervalSeconds));
            await _taskLaunchService.StartGlobalLeakAsync(plan, TaskLaunchSource.Desktop);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"启动全域捡漏失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("启动全域捡漏失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task StopGlobalLeakAsync()
    {
        try
        {
            await _globalLeakCoordinator.StopAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"停止全域捡漏失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("停止全域捡漏失败", ex.Message);
        }
    }

    private async Task LoadLibrariesForSelectionAsync()
    {
        try
        {
            var result = await _venueWorkflowService.LoadLibrariesAsync(
                restorePreferredSelection: false);
            PopulateLibraries(result.Libraries);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"加载全域捡漏场馆列表失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("加载扫描场馆失败", ex.Message);
        }
    }

    private async Task PersistGlobalLeakLibrarySelectionAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> selectedLibraries,
        CancellationToken cancellationToken = default)
    {
        await _settingsWorkflowService.SaveGlobalLeakSelectedLibrariesAsync(selectedLibraries, cancellationToken);
    }

    private async Task<bool> TryPersistGlobalLeakLibrarySelectionAsync(
        IReadOnlyList<GlobalLeakLibraryTarget> selectedLibraries,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PersistGlobalLeakLibrarySelectionAsync(selectedLibraries, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"保存全域捡漏扫描场馆失败：{ex.Message}", ex);
            await _notificationService.ShowWarningAsync("保存扫描场馆失败", ex.Message, cancellationToken);
            return false;
        }
    }

    private void OnLibrarySelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoGlobalLeakLibraries));
        OnPropertyChanged(nameof(SelectedGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(HasSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasDraftGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoDraftGlobalLeakLibraries));
        OnPropertyChanged(nameof(SelectedGlobalLeakLibrarySummaryText));
        OnPropertyChanged(nameof(DraftGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(DraftGlobalLeakLibrarySummaryText));
    }

    private void OnGlobalLeakStatusChanged(object? sender, CoordinatorStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyGlobalLeakStatus(status);
            TryRecordGlobalLeakSuccess(status);
            if (status.State == CoordinatorTaskState.Completed &&
                status.Reason == CoordinatorStatusReason.GlobalLeakSucceeded)
            {
                _ = RefreshGlobalLeakSuccessReservationAsync();
            }
        });
    }

    private async Task RefreshGlobalLeakSuccessReservationAsync()
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
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"全域捡漏成功后刷新预约状态失败：{ex.Message}", ex);
        }
    }

    private void ApplyGlobalLeakStatus(CoordinatorStatus status)
    {
        GlobalLeakStatusText = status.Message;
        IsGlobalLeakTaskActive = IsTaskActive(status);
        GlobalLeakScanRoundCount = status.PollCount;
        GlobalLeakRequestCount = status.RequestCount;
        _globalLeakLastRequestAt = status.LastRequestAt;
        _globalLeakTaskState = status.State;
        _globalLeakStatusReason = status.Reason;
        UpdateGlobalLeakLastRequestText();
        ApplyGlobalLeakRuntime(status);
        _statusApplied?.Invoke(status);
        OnPropertyChanged(nameof(GlobalLeakDashboardStatusText));
        OnPropertyChanged(nameof(GlobalLeakDashboardStatusBrush));
    }

    private void UpdateGlobalLeakLastRequestText()
    {
        if (_globalLeakLastRequestAt is null)
        {
            GlobalLeakLastRequestText = "无";
            return;
        }

        var elapsed = GetCurrentTime() - _globalLeakLastRequestAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        GlobalLeakLastRequestText = elapsed < TimeSpan.FromSeconds(1)
            ? "刚刚"
            : $"{Math.Max(1, (int)Math.Floor(elapsed.TotalSeconds))} 秒前";
    }

    private void ApplyGlobalLeakRuntime(CoordinatorStatus status)
    {
        switch (status.State)
        {
            case CoordinatorTaskState.Idle:
            case CoordinatorTaskState.Starting:
                ResetGlobalLeakRuntime();
                return;
            case CoordinatorTaskState.Running:
                _globalLeakRuntimeStartedAt ??= status.LastUpdatedAt ?? GetCurrentTime();
                UpdateGlobalLeakRuntimeText(GetCurrentTime());
                return;
            case CoordinatorTaskState.Stopping:
            case CoordinatorTaskState.Completed:
            case CoordinatorTaskState.Failed:
                FreezeGlobalLeakRuntime(status.LastUpdatedAt);
                return;
        }
    }

    private void FreezeGlobalLeakRuntime(DateTimeOffset? stoppedAt)
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            return;
        }

        UpdateGlobalLeakRuntimeText(stoppedAt ?? GetCurrentTime());
        _globalLeakRuntimeStartedAt = null;
    }

    private void ResetGlobalLeakRuntime()
    {
        _globalLeakRuntimeStartedAt = null;
        GlobalLeakRuntimeText = "00:00:00";
    }

    private void UpdateGlobalLeakRuntimeText(DateTimeOffset timestamp)
    {
        if (_globalLeakRuntimeStartedAt is null)
        {
            GlobalLeakRuntimeText = "00:00:00";
            return;
        }

        GlobalLeakRuntimeText = FormatElapsedClock(timestamp - _globalLeakRuntimeStartedAt.Value);
    }

    private void TryRecordGlobalLeakSuccess(CoordinatorStatus status)
    {
        if (status.State != CoordinatorTaskState.Completed ||
            status.Reason != CoordinatorStatusReason.GlobalLeakSucceeded)
        {
            return;
        }

        var recordedAt = status.LastUpdatedAt ?? GetCurrentTime();
        if (_lastRecordedGlobalLeakSuccessAt == recordedAt)
        {
            return;
        }

        _lastRecordedGlobalLeakSuccessAt = recordedAt;
        if (_recordSuccessfulReservationAsync is not null)
        {
            _ = _recordSuccessfulReservationAsync();
        }
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

    private static string FormatElapsedClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{Math.Max(0, (int)elapsed.TotalHours):D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
