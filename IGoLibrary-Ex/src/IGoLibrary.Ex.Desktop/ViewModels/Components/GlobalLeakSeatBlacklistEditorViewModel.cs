using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class GlobalLeakSeatBlacklistEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IGlobalLeakSeatBlacklistService _blacklistService;
    private readonly IVenueWorkflowService _venueService;
    private readonly ISeatLabelService _labelService;
    private readonly IActivityLogService _activityLog;
    private readonly INotificationService _notifications;
    private readonly ILogger<GlobalLeakSeatBlacklistEditorViewModel> _logger;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _layoutRequest;
    private long _sessionVersion;
    private long _layoutVersion;
    private bool _synchronizing;
    private bool _restoringVenue;

    public GlobalLeakSeatBlacklistEditorViewModel(IGlobalLeakSeatBlacklistService blacklistService,
        IVenueWorkflowService venueService, ISeatLabelService labelService,
        IActivityLogService activityLog, INotificationService notifications,
        ILogger<GlobalLeakSeatBlacklistEditorViewModel> logger, ILoggerFactory? loggerFactory = null,
        SeatViewPreferenceService? viewPreferences = null)
    {
        _blacklistService = blacklistService;
        _venueService = venueService;
        _labelService = labelService;
        _activityLog = activityLog;
        _notifications = notifications;
        _logger = logger;
        Workspace = new SeatWorkspaceViewModel(activityLog, loggerFactory?.CreateLogger<SeatWorkspaceViewModel>(), viewPreferences);
    }

    public SeatWorkspaceViewModel Workspace { get; }
    public ObservableCollection<GlobalLeakBlacklistVenueViewModel> Venues { get; } = [];
    public ObservableCollection<SeatReference> MissingSeats { get; } = [];
    public Task PendingLoad { get; private set; } = Task.CompletedTask;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private bool isSettingsLoaded;
    [ObservableProperty] private bool isTaskActive;
    [ObservableProperty] private string errorText = string.Empty;
    [ObservableProperty] private GlobalLeakBlacklistVenueViewModel? selectedVenue;

    public bool HasError => ErrorText.Length > 0;
    public bool HasMissingSeats => MissingSeats.Count > 0;
    public bool CanClose => !IsSaving;
    public bool CanSwitchVenue => IsSettingsLoaded && !IsSaving;
    public bool CanRefresh => IsOpen && !IsSaving && !IsLoading;
    public bool CanSave => IsOpen && IsSettingsLoaded && !IsTaskActive && !IsSaving && !IsLoading;
    public bool CanEditSeats => CanSave && SelectedVenue?.Layout is not null && !HasError;
    public string SummaryText => $"当前场馆 {SelectedVenue?.Draft.Count ?? 0} 个，本次所选场馆共 {Venues.Sum(static venue => venue.Draft.Count)} 个黑名单座位";

    partial void OnIsOpenChanged(bool value) => NotifyAvailability();
    partial void OnIsLoadingChanged(bool value) => NotifyAvailability();
    partial void OnIsSavingChanged(bool value) => NotifyAvailability();
    partial void OnIsSettingsLoadedChanged(bool value) => NotifyAvailability();
    partial void OnIsTaskActiveChanged(bool value) => NotifyAvailability();
    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        NotifyAvailability();
    }
    partial void OnSelectedVenueChanged(GlobalLeakBlacklistVenueViewModel? oldValue, GlobalLeakBlacklistVenueViewModel? newValue)
    {
        if (_restoringVenue) return;
        if (IsOpen && (IsSaving || newValue is not null && !Venues.Contains(newValue)))
        {
            _restoringVenue = true;
            SelectedVenue = oldValue;
            _restoringVenue = false;
            return;
        }
        if (IsOpen && IsSettingsLoaded && newValue is not null)
            PendingLoad = LoadLayoutAsync(newValue, refresh: false);
    }

    public async Task OpenAsync(IReadOnlyList<GlobalLeakLibraryTarget> libraries)
    {
        if (IsOpen || IsTaskActive || libraries.Count == 0) return;
        ResetSession();
        _lifetime = new CancellationTokenSource();
        foreach (var library in libraries.DistinctBy(static library => library.LibraryId))
            Venues.Add(new GlobalLeakBlacklistVenueViewModel(library));
        IsOpen = true;
        Workspace.SeatFilterText = string.Empty;
        Workspace.ShowAvailableOnly = false;
        _logger.LogInformation("打开全域捡漏黑名单编辑器。场馆数量={LibraryCount}。", Venues.Count);
        await LoadBlacklistAsync();
    }

    private async Task LoadBlacklistAsync()
    {
        var session = _sessionVersion;
        var token = _lifetime!.Token;
        IsLoading = true;
        ErrorText = string.Empty;
        try
        {
            var saved = await _blacklistService.LoadAsync(Venues.Select(static venue => venue.Target.LibraryId).ToArray(), token);
            if (!IsCurrentSession(session)) return;
            foreach (var venue in Venues)
                venue.Restore(saved.GetValueOrDefault(venue.Target.LibraryId) ?? []);
            IsSettingsLoaded = true;
            IsLoading = false;
            SelectedVenue = Venues[0];
            await PendingLoad;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrentSession(session)) return;
            ErrorText = "读取黑名单失败，请重试";
            await ReportFailureAsync("读取黑名单失败", ex);
        }
        finally
        {
            if (IsCurrentSession(session) && !IsSettingsLoaded) IsLoading = false;
        }
    }

    private async Task LoadLayoutAsync(GlobalLeakBlacklistVenueViewModel venue, bool refresh)
    {
        _layoutRequest?.Cancel();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        _layoutRequest = request;
        var token = request.Token;
        var version = ++_layoutVersion;
        var session = _sessionVersion;
        IsLoading = true;
        ErrorText = string.Empty;
        if (Workspace.LibraryId != venue.Target.LibraryId) ClearWorkspace();
        try
        {
            if (refresh || venue.Layout is null)
            {
                var layout = await _venueService.LoadSeatLayoutAsync(venue.Target.LibraryId, token);
                if (!IsCurrentLayout(session, version)) return;
                if (layout.LibraryId != venue.Target.LibraryId)
                    throw new InvalidOperationException("返回的座位布局与所选场馆不一致");
                IReadOnlyList<SeatLabel> labels = [];
                try { labels = await _labelService.GetLabelsAsync(venue.Target.LibraryId, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    if (!IsCurrentLayout(session, version)) return;
                    _logger.LogWarning(ex, "黑名单选座标签读取失败。场馆标识={LibraryId}。", venue.Target.LibraryId);
                    await _notifications.ShowWarningAsync("标签暂不可用", "座位仍可选择，刷新后重试加载标签", token);
                }
                if (!IsCurrentLayout(session, version)) return;
                venue.Layout = layout;
                venue.Labels = labels;
                _logger.LogDebug("黑名单选座布局已加载。场馆标识={LibraryId}，座位数量={SeatCount}。",
                    venue.Target.LibraryId, layout.Seats.Count);
            }
            await PopulateWorkspaceAsync(venue);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrentLayout(session, version)) return;
            ErrorText = "加载座位失败，请刷新重试或切换场馆";
            await ReportFailureAsync("加载黑名单座位失败", ex);
        }
        finally
        {
            if (IsCurrentLayout(session, version))
            {
                _layoutRequest = null;
                IsLoading = false;
            }
            else _logger.LogDebug("已丢弃过期的黑名单座位请求。场馆标识={LibraryId}。", venue.Target.LibraryId);
            request.Dispose();
        }
    }

    private async Task PopulateWorkspaceAsync(GlobalLeakBlacklistVenueViewModel venue)
    {
        Workspace.CancelFiltering();
        foreach (var oldSeat in Workspace.Seats) oldSeat.PropertyChanged -= OnSeatChanged;
        Workspace.Seats.Clear();
        var labels = venue.Labels.DistinctBy(static label => label.SeatKey, StringComparer.Ordinal)
            .ToDictionary(static label => label.SeatKey, StringComparer.Ordinal);
        foreach (var seat in venue.Layout!.Seats)
        {
            var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied)
            {
                SeatStatus = seat.SeatStatus,
                IsSelected = venue.Draft.ContainsKey(seat.SeatKey),
                LabelText = labels.GetValueOrDefault(seat.SeatKey)?.Text
            };
            item.PropertyChanged += OnSeatChanged;
            Workspace.Seats.Add(item);
        }
        Workspace.ApplyLayout(venue.Layout);
        UpdateSelectionPresentation();
        await Workspace.RefreshAsync();
    }

    private void OnSeatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_synchronizing || e.PropertyName != nameof(SeatItemViewModel.IsSelected) ||
            sender is not SeatItemViewModel seat || SelectedVenue is not { } venue) return;
        if (!CanEditSeats)
        {
            _synchronizing = true;
            seat.IsSelected = venue.Draft.ContainsKey(seat.SeatKey);
            _synchronizing = false;
            return;
        }
        if (seat.IsSelected) venue.Draft[seat.SeatKey] = new SeatReference(seat.SeatKey, seat.SeatName);
        else venue.Draft.Remove(seat.SeatKey);
        UpdateSelectionPresentation();
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        if (!CanRefresh) return Task.CompletedTask;
        return PendingLoad = !IsSettingsLoaded ? LoadBlacklistAsync()
            : SelectedVenue is { } venue ? LoadLayoutAsync(venue, refresh: true) : Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearCurrentVenue()
    {
        if (!CanEditSeats || SelectedVenue is null) return;
        SelectedVenue.Draft.Clear();
        _synchronizing = true;
        foreach (var seat in Workspace.Seats) seat.IsSelected = false;
        _synchronizing = false;
        UpdateSelectionPresentation();
    }

    [RelayCommand]
    private void RemoveMissingSeat(SeatReference? seat)
    {
        if (!CanEditSeats || seat is null || SelectedVenue is null) return;
        SelectedVenue.Draft.Remove(seat.SeatKey);
        UpdateSelectionPresentation();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave) return;
        var session = _sessionVersion;
        var token = _lifetime!.Token;
        var changes = Venues.Where(static venue => venue.IsDirty).ToDictionary(
            static venue => venue.Target.LibraryId,
            static venue => (IReadOnlyList<SeatReference>)venue.Draft.Values.ToArray());
        IsSaving = true;
        try
        {
            await _blacklistService.SaveAsync(changes, token);
            if (!IsCurrentSession(session)) return;
            ResetSession();
            await _notifications.ShowSuccessAsync("黑名单已保存", "全域捡漏将跳过已选择的黑名单座位");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentSession(session)) await ReportFailureAsync("保存黑名单失败", ex);
        }
        finally { if (IsCurrentSession(session)) IsSaving = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!CanClose) return;
        _logger.LogDebug("已关闭黑名单编辑器，放弃未保存的草稿。");
        ResetSession();
    }

    public void ResetSession()
    {
        ++_sessionVersion;
        ++_layoutVersion;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _layoutRequest?.Cancel();
        _layoutRequest = null;
        IsOpen = false;
        IsSettingsLoaded = false;
        IsSaving = false;
        IsLoading = false;
        SelectedVenue = null;
        Venues.Clear();
        ClearWorkspace();
        ErrorText = string.Empty;
    }

    private bool IsCurrentSession(long session) => IsOpen && session == _sessionVersion;
    private bool IsCurrentLayout(long session, long version) => IsCurrentSession(session) && version == _layoutVersion;
    private void ClearWorkspace()
    {
        foreach (var seat in Workspace.Seats) seat.PropertyChanged -= OnSeatChanged;
        Workspace.Clear();
        MissingSeats.Clear();
        OnPropertyChanged(nameof(HasMissingSeats));
        OnPropertyChanged(nameof(SummaryText));
    }
    private void UpdateSelectionPresentation()
    {
        MissingSeats.Clear();
        if (SelectedVenue is { } venue)
        {
            venue.NotifyCountChanged();
            var keys = Workspace.Seats.Select(static seat => seat.SeatKey).ToHashSet(StringComparer.Ordinal);
            foreach (var seat in venue.Draft.Values.Where(seat => !keys.Contains(seat.SeatKey))) MissingSeats.Add(seat);
        }
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasMissingSeats));
    }
    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanSwitchVenue));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanEditSeats));
    }
    private async Task ReportFailureAsync(string title, Exception ex)
    {
        _activityLog.Write(LogEntryKind.Warning, "GlobalLeak", $"{title}：{ex.Message}", ex);
        await _notifications.ShowWarningAsync(title, ex.Message);
    }
    public void Dispose()
    {
        ResetSession();
        Workspace.Dispose();
    }
}
