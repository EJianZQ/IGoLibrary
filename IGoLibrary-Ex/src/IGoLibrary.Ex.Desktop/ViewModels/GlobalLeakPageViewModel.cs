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
    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly INotificationService _notificationService;
    private readonly IAppThemeService _appThemeService;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<int> _committedGlobalLeakLibraryIds = [];
    private readonly HashSet<int> _draftGlobalLeakLibraryIds = [];

    private Func<bool, Task>? _loadLibrariesAsync;
    private Func<bool>? _isAuthorized;
    private Func<Task>? _refreshSuccessReservationAsync;
    private Func<Task>? _recordSuccessfulReservationAsync;
    private Action<CoordinatorStatus>? _statusApplied;
    private bool _isSynchronizingGlobalLeakLibrarySelection;
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
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        INotificationService notificationService,
        IAppThemeService appThemeService,
        TimeProvider timeProvider)
    {
        _globalLeakCoordinator = globalLeakCoordinator;
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

    public ObservableCollection<GlobalLeakLibraryItemViewModel> GlobalLeakLibraries { get; } = [];

    public ObservableCollection<GlobalLeakLibraryTarget> SelectedGlobalLeakLibraries { get; } = [];

    [ObservableProperty]
    private bool isGlobalLeakLibraryPickerOpen;

    [ObservableProperty]
    private string globalLeakStatusText = "未运行";

    [ObservableProperty]
    private bool isGlobalLeakTaskActive;

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

    public bool HasGlobalLeakLibraries => GlobalLeakLibraries.Count > 0;

    public bool HasNoGlobalLeakLibraries => !HasGlobalLeakLibraries;

    public int SelectedGlobalLeakLibraryCount => SelectedGlobalLeakLibraries.Count;

    public bool HasSelectedGlobalLeakLibraries => SelectedGlobalLeakLibraryCount > 0;

    public bool HasNoSelectedGlobalLeakLibraries => !HasSelectedGlobalLeakLibraries;

    public bool CanEditGlobalLeakConfiguration => !IsGlobalLeakTaskActive;

    public string SelectedGlobalLeakLibrarySummaryText => HasSelectedGlobalLeakLibraries
        ? $"已选 {SelectedGlobalLeakLibraryCount} 个扫描场馆"
        : "尚未选择扫描场馆";

    public int DraftGlobalLeakLibraryCount => _draftGlobalLeakLibraryIds.Count;

    public string DraftGlobalLeakLibrarySummaryText => DraftGlobalLeakLibraryCount > 0
        ? $"本次已勾选 {DraftGlobalLeakLibraryCount} 个场馆"
        : "本次尚未勾选场馆";

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
        Func<bool, Task> loadLibrariesAsync,
        Func<bool> isAuthorized,
        Func<Task> refreshSuccessReservationAsync,
        Func<Task> recordSuccessfulReservationAsync,
        Action<CoordinatorStatus> statusApplied)
    {
        _loadLibrariesAsync = loadLibrariesAsync;
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
        var selectedIdsToRestore = IsGlobalLeakLibraryPickerOpen
            ? _draftGlobalLeakLibraryIds.ToArray()
            : _committedGlobalLeakLibraryIds.ToArray();

        ClearLibraries(keepSelection: true);
        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in libraries)
            {
                var item = new GlobalLeakLibraryItemViewModel(library)
                {
                    IsSelected = selectedIdsToRestore.Contains(library.LibraryId)
                };
                item.PropertyChanged += OnGlobalLeakLibraryItemPropertyChanged;
                GlobalLeakLibraries.Add(item);
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
        }
        else
        {
            RefreshSelectedGlobalLeakLibrariesPresentation();
        }

        OnPropertyChanged(nameof(HasGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoGlobalLeakLibraries));
    }

    public void ClearLibraries(bool keepSelection = false)
    {
        foreach (var library in GlobalLeakLibraries)
        {
            library.PropertyChanged -= OnGlobalLeakLibraryItemPropertyChanged;
        }

        GlobalLeakLibraries.Clear();
        if (!keepSelection)
        {
            _draftGlobalLeakLibraryIds.Clear();
            _committedGlobalLeakLibraryIds.Clear();
            RefreshSelectedGlobalLeakLibrariesPresentation();
            UpdateDraftGlobalLeakLibrarySelectionPresentation();
        }

        OnPropertyChanged(nameof(HasGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoGlobalLeakLibraries));
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
            if (storedLibraries.Count == 0)
            {
                _committedGlobalLeakLibraryIds.Clear();
                ApplyGlobalLeakLibrarySelectionToItems(Array.Empty<int>());
                RefreshSelectedGlobalLeakLibrariesPresentation();
                _globalLeakSelectionRestoredForCurrentSession = true;
                return;
            }

            var availableLibraryIds = GlobalLeakLibraries
                .Select(static library => library.LibraryId)
                .ToHashSet();
            var restoredIds = storedLibraries
                .Select(static library => library.LibraryId)
                .Where(availableLibraryIds.Contains)
                .Distinct()
                .ToArray();
            var skippedCount = storedLibraries
                .Select(static library => library.LibraryId)
                .Distinct()
                .Count(libraryId => !availableLibraryIds.Contains(libraryId));

            _committedGlobalLeakLibraryIds.Clear();
            foreach (var libraryId in restoredIds)
            {
                _committedGlobalLeakLibraryIds.Add(libraryId);
            }

            ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
            RefreshSelectedGlobalLeakLibrariesPresentation();
            UpdateDraftGlobalLeakLibrarySelectionPresentation();

            if (restoredIds.Length > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"已恢复 {restoredIds.Length} 个全域捡漏扫描场馆。");
            }

            if (skippedCount > 0)
            {
                _activityLogService.Write(LogEntryKind.Info, "GlobalLeak", $"有 {skippedCount} 个历史全域捡漏场馆不在当前账号场馆列表中，已跳过。");
            }

            _globalLeakSelectionRestoredForCurrentSession = true;
        }
        catch (Exception ex)
        {
            _globalLeakSelectionRestoredForCurrentSession = false;
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"恢复全域捡漏扫描场馆失败：{ex.Message}");
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

        BeginGlobalLeakLibrarySelectionDraft();
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
        if (IsGlobalLeakLibraryPickerOpen)
        {
            ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
            UpdateDraftGlobalLeakLibrarySelectionPresentation();
        }
    }

    [RelayCommand]
    private async Task ConfirmGlobalLeakLibrariesAsync()
    {
        if (!CanEditGlobalLeakConfiguration)
        {
            return;
        }

        var selectedLibraries = CreateGlobalLeakLibrarySelectionSnapshotFromItems();
        if (!await TryPersistGlobalLeakLibrarySelectionAsync(selectedLibraries))
        {
            return;
        }

        CommitGlobalLeakLibrarySelection();
        IsGlobalLeakLibraryPickerOpen = false;
    }

    [RelayCommand]
    private void CancelGlobalLeakLibraries()
    {
        RestoreCommittedGlobalLeakLibrarySelection();
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
        }

        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in GlobalLeakLibraries)
            {
                library.IsSelected = true;
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
            return;
        }

        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
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

        _draftGlobalLeakLibraryIds.Clear();
        _committedGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(Array.Empty<int>());
        RefreshSelectedGlobalLeakLibrariesPresentation();
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
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

        _draftGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    [RelayCommand]
    private async Task RemoveSelectedGlobalLeakLibraryAsync(GlobalLeakLibraryTarget? target)
    {
        if (target is null || !CanEditGlobalLeakConfiguration)
        {
            return;
        }

        if (!_committedGlobalLeakLibraryIds.Remove(target.LibraryId))
        {
            return;
        }

        var nextLibraries = CreateGlobalLeakLibrarySelectionSnapshot(_committedGlobalLeakLibraryIds);
        if (!await TryPersistGlobalLeakLibrarySelectionAsync(nextLibraries))
        {
            _committedGlobalLeakLibraryIds.Add(target.LibraryId);
            return;
        }

        RefreshSelectedGlobalLeakLibrariesPresentation();
        if (!IsGlobalLeakLibraryPickerOpen)
        {
            ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
        }
    }

    [RelayCommand]
    private async Task StartGlobalLeakAsync()
    {
        if (IsGlobalLeakTaskActive)
        {
            return;
        }

        var selectedLibraries = SelectedGlobalLeakLibraries.ToList();
        if (selectedLibraries.Count == 0)
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
            await _globalLeakCoordinator.StartAsync(plan);
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"启动全域捡漏失败：{ex.Message}");
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
            _activityLogService.Write(LogEntryKind.Error, "GlobalLeak", $"停止全域捡漏失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("停止全域捡漏失败", ex.Message);
        }
    }

    private async Task LoadLibrariesForSelectionAsync()
    {
        if (_loadLibrariesAsync is not null)
        {
            await _loadLibrariesAsync(false);
        }
    }

    private void OnGlobalLeakLibraryItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingGlobalLeakLibrarySelection ||
            e.PropertyName != nameof(GlobalLeakLibraryItemViewModel.IsSelected))
        {
            return;
        }

        if (IsGlobalLeakLibraryPickerOpen)
        {
            RefreshDraftGlobalLeakLibrarySelectionFromItems();
            return;
        }

        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
        _ = PersistGlobalLeakLibrarySelectionSafelyAsync();
    }

    private async Task PersistGlobalLeakLibrarySelectionAsync(CancellationToken cancellationToken = default)
    {
        await PersistGlobalLeakLibrarySelectionAsync(
            SelectedGlobalLeakLibraries.ToArray(),
            cancellationToken);
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
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"保存全域捡漏扫描场馆失败：{ex.Message}");
            await _notificationService.ShowWarningAsync("保存扫描场馆失败", ex.Message, cancellationToken);
            return false;
        }
    }

    private async Task PersistGlobalLeakLibrarySelectionSafelyAsync()
    {
        try
        {
            await PersistGlobalLeakLibrarySelectionAsync();
        }
        catch (Exception ex)
        {
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"保存全域捡漏扫描场馆失败：{ex.Message}");
        }
    }

    private void BeginGlobalLeakLibrarySelectionDraft()
    {
        _draftGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in _committedGlobalLeakLibraryIds)
        {
            _draftGlobalLeakLibraryIds.Add(libraryId);
        }

        ApplyGlobalLeakLibrarySelectionToItems(_draftGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void CommitGlobalLeakLibrarySelection()
    {
        RefreshCommittedGlobalLeakLibrarySelectionFromItems();
        _draftGlobalLeakLibraryIds.Clear();
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RestoreCommittedGlobalLeakLibrarySelection()
    {
        _draftGlobalLeakLibraryIds.Clear();
        ApplyGlobalLeakLibrarySelectionToItems(_committedGlobalLeakLibraryIds);
        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RefreshDraftGlobalLeakLibrarySelectionFromItems()
    {
        _draftGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in GlobalLeakLibraries.Where(static library => library.IsSelected).Select(static library => library.LibraryId))
        {
            _draftGlobalLeakLibraryIds.Add(libraryId);
        }

        UpdateDraftGlobalLeakLibrarySelectionPresentation();
    }

    private void RefreshCommittedGlobalLeakLibrarySelectionFromItems()
    {
        _committedGlobalLeakLibraryIds.Clear();
        foreach (var libraryId in GlobalLeakLibraries.Where(static library => library.IsSelected).Select(static library => library.LibraryId))
        {
            _committedGlobalLeakLibraryIds.Add(libraryId);
        }

        RefreshSelectedGlobalLeakLibrariesPresentation();
    }

    private void RefreshSelectedGlobalLeakLibrariesPresentation()
    {
        SelectedGlobalLeakLibraries.Clear();
        foreach (var library in EnumerateSelectedGlobalLeakLibraries(_committedGlobalLeakLibraryIds))
        {
            SelectedGlobalLeakLibraries.Add(library);
        }

        OnPropertyChanged(nameof(SelectedGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(HasSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(HasNoSelectedGlobalLeakLibraries));
        OnPropertyChanged(nameof(SelectedGlobalLeakLibrarySummaryText));
    }

    private void UpdateDraftGlobalLeakLibrarySelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftGlobalLeakLibraryCount));
        OnPropertyChanged(nameof(DraftGlobalLeakLibrarySummaryText));
    }

    private IEnumerable<GlobalLeakLibraryTarget> EnumerateSelectedGlobalLeakLibraries(IReadOnlySet<int> selectedLibraryIds)
    {
        return GlobalLeakLibraries
            .Where(library => selectedLibraryIds.Contains(library.LibraryId))
            .Select(static library => new GlobalLeakLibraryTarget(
                library.LibraryId,
                library.LibraryName,
                library.Floor));
    }

    private GlobalLeakLibraryTarget[] CreateGlobalLeakLibrarySelectionSnapshot(IEnumerable<int> selectedLibraryIds)
    {
        var selectedIds = selectedLibraryIds as IReadOnlySet<int> ?? new HashSet<int>(selectedLibraryIds);
        return EnumerateSelectedGlobalLeakLibraries(selectedIds).ToArray();
    }

    private GlobalLeakLibraryTarget[] CreateGlobalLeakLibrarySelectionSnapshotFromItems()
    {
        return GlobalLeakLibraries
            .Where(static library => library.IsSelected)
            .Select(static library => new GlobalLeakLibraryTarget(
                library.LibraryId,
                library.LibraryName,
                library.Floor))
            .ToArray();
    }

    private void ApplyGlobalLeakLibrarySelectionToItems(IEnumerable<int> selectedLibraryIds)
    {
        var selectedIds = selectedLibraryIds as IReadOnlySet<int> ?? new HashSet<int>(selectedLibraryIds);
        _isSynchronizingGlobalLeakLibrarySelection = true;
        try
        {
            foreach (var library in GlobalLeakLibraries)
            {
                library.IsSelected = selectedIds.Contains(library.LibraryId);
            }
        }
        finally
        {
            _isSynchronizingGlobalLeakLibrarySelection = false;
        }
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
            _activityLogService.Write(LogEntryKind.Warning, "GlobalLeak", $"全域捡漏成功后刷新预约状态失败：{ex.Message}");
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
