using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class MultiSeatSelectionViewModel(
    IVenueWorkflowService venueWorkflowService,
    IActivityLogService activityLogService,
    INotificationService notificationService) : ViewModelBase
{
    private readonly ObservableCollection<SeatItemViewModel> _allSeats = [];
    private readonly object _filterGate = new();
    private readonly HashSet<string> _committedSelectedSeatKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _draftSelectedSeatKeys = new(StringComparer.Ordinal);
    private CancellationTokenSource? _filteringCts;
    private bool _isSynchronizingSeatSelection;
    private Func<LibrarySummary?>? _selectedLibrary;
    private Func<bool>? _canEditGrabConfiguration;
    private Func<bool>? _isGrabSeatSelectionOverlayOpen;

    public ObservableCollection<SeatItemViewModel> VisibleSeats { get; } = [];

    public ObservableCollection<SeatReference> SelectedSeats { get; } = [];

    [ObservableProperty]
    private string seatFilterText = string.Empty;

    [ObservableProperty]
    private bool showAvailableOnly;

    [ObservableProperty]
    private bool isApplyingSeatFilter;

    [ObservableProperty]
    private int visibleSeatResultCount;

    public int SeatCount => _allSeats.Count;

    public int SelectedSeatCount => SelectedSeats.Count;

    public bool HasSelectedSeats => SelectedSeatCount > 0;

    public bool HasNoSelectedSeats => !HasSelectedSeats;

    public int DraftSelectedSeatCount => _draftSelectedSeatKeys.Count;

    public bool HasVisibleSeatResults => VisibleSeatResultCount > 0;

    public bool HasNoVisibleSeatResults => !HasVisibleSeatResults;

    public bool HasSeatLayout => _allSeats.Count > 0;

    public bool HasNoSeatLayout => !HasSeatLayout;

    public bool ShowSeatFilterEmptyState => HasSeatLayout && HasNoVisibleSeatResults;

    public string SelectedSeatSummaryText => HasSelectedSeats
        ? $"已选 {SelectedSeatCount} 个目标座位"
        : "尚未选择目标座位";

    public string SelectedSeatHintText => HasSelectedSeats
        ? "这些座位会被持续监控，任意一个释放后都会立即尝试预约。"
        : "点击上方按钮打开选座工作区，确认后才会同步到主界面。";

    public string DraftSelectedSeatSummaryText => DraftSelectedSeatCount > 0
        ? $"本次已勾选 {DraftSelectedSeatCount} 个目标座位"
        : "本次尚未勾选目标座位";

    public void Configure(
        Func<LibrarySummary?> selectedLibrary,
        Func<bool> canEditGrabConfiguration,
        Func<bool> isGrabSeatSelectionOverlayOpen)
    {
        _selectedLibrary = selectedLibrary;
        _canEditGrabConfiguration = canEditGrabConfiguration;
        _isGrabSeatSelectionOverlayOpen = isGrabSeatSelectionOverlayOpen;
    }

    public IReadOnlyList<SeatReference> GetSelectedSeatSnapshot()
    {
        return SelectedSeats.ToArray();
    }

    public void BeginDraft()
    {
        _draftSelectedSeatKeys.Clear();
        foreach (var seatKey in _committedSelectedSeatKeys)
        {
            _draftSelectedSeatKeys.Add(seatKey);
        }

        ApplySelectionToSeatItems(_draftSelectedSeatKeys);
        UpdateDraftSelectionPresentation();
    }

    public void CommitDraft()
    {
        RefreshCommittedSelectionFromCurrentItems();
        _draftSelectedSeatKeys.Clear();
        UpdateDraftSelectionPresentation();
    }

    public void RestoreCommittedSelection()
    {
        ApplySelectionToSeatItems(_committedSelectedSeatKeys);
        _draftSelectedSeatKeys.Clear();
        UpdateDraftSelectionPresentation();
    }

    public async Task PopulateSeatsAsync(LibraryLayout layout, bool preserveSelection)
    {
        CancelFiltering();
        var selectedKeysToRestore = preserveSelection
            ? IsGrabSeatSelectionOverlayOpen()
                ? _draftSelectedSeatKeys.ToArray()
                : _committedSelectedSeatKeys.ToArray()
            : Array.Empty<string>();

        if (!preserveSelection)
        {
            _draftSelectedSeatKeys.Clear();
            _committedSelectedSeatKeys.Clear();
        }

        foreach (var seat in _allSeats)
        {
            seat.PropertyChanged -= OnSeatItemPropertyChanged;
        }

        _allSeats.Clear();
        VisibleSeats.Clear();
        _isSynchronizingSeatSelection = true;
        foreach (var seat in layout.Seats)
        {
            var item = new SeatItemViewModel(seat.SeatKey, seat.SeatName, seat.IsOccupied);
            item.PropertyChanged += OnSeatItemPropertyChanged;
            item.IsSelected = selectedKeysToRestore.Contains(item.SeatKey, StringComparer.Ordinal);
            _allSeats.Add(item);
            VisibleSeats.Add(item);
        }
        _isSynchronizingSeatSelection = false;

        await ApplySeatFilterAsync();
        OnPropertyChanged(nameof(SeatCount));
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));

        if (IsGrabSeatSelectionOverlayOpen())
        {
            RefreshDraftSelectionFromCurrentItems();
        }
        else if (preserveSelection)
        {
            RefreshSelectedSeatsPresentation();
        }
        else
        {
            RefreshSelectedSeatsPresentation();
            UpdateDraftSelectionPresentation();
        }
    }

    public void ClearSeats()
    {
        CancelFiltering();
        foreach (var seat in _allSeats)
        {
            seat.PropertyChanged -= OnSeatItemPropertyChanged;
        }

        _allSeats.Clear();
        VisibleSeats.Clear();
        _committedSelectedSeatKeys.Clear();
        _draftSelectedSeatKeys.Clear();
        VisibleSeatResultCount = 0;
        RefreshSelectedSeatsPresentation();
        UpdateDraftSelectionPresentation();
        OnPropertyChanged(nameof(SeatCount));
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
    }

    public void ApplyFavoriteStates(IEnumerable<string> favoriteSeatKeys, bool syncSelection)
    {
        var favoriteKeys = favoriteSeatKeys.ToHashSet(StringComparer.Ordinal);

        foreach (var seat in _allSeats)
        {
            var isFavorite = favoriteKeys.Contains(seat.SeatKey);
            seat.IsFavorite = isFavorite;

            if (syncSelection)
            {
                seat.IsSelected = isFavorite;
            }
        }

        if (syncSelection)
        {
            if (IsGrabSeatSelectionOverlayOpen())
            {
                RefreshDraftSelectionFromCurrentItems();
            }
            else
            {
                RefreshCommittedSelectionFromCurrentItems();
            }
        }
    }

    public void CancelFiltering()
    {
        lock (_filterGate)
        {
            if (_filteringCts is null)
            {
                return;
            }

            _filteringCts.Cancel();
            _filteringCts.Dispose();
            _filteringCts = null;
        }
    }

    partial void OnVisibleSeatResultCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasVisibleSeatResults));
        OnPropertyChanged(nameof(HasNoVisibleSeatResults));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
    }

    partial void OnSeatFilterTextChanged(string value) => _ = ApplySeatFilterAsync();

    partial void OnShowAvailableOnlyChanged(bool value) => _ = ApplySeatFilterAsync();

    [RelayCommand]
    private void RemoveSelectedSeat(SeatReference? seat)
    {
        if (seat is null || !CanEditGrabConfiguration())
        {
            return;
        }

        if (!_committedSelectedSeatKeys.Remove(seat.SeatKey))
        {
            return;
        }

        RefreshSelectedSeatsPresentation();
        if (!IsGrabSeatSelectionOverlayOpen())
        {
            ApplySelectionToSeatItems(_committedSelectedSeatKeys);
        }
    }

    [RelayCommand]
    private async Task SaveFavoritesAsync()
    {
        var selectedLibrary = _selectedLibrary?.Invoke();
        if (selectedLibrary is null)
        {
            return;
        }

        try
        {
            var selected = _allSeats
                .Where(x => x.IsSelected)
                .Select(x => new SeatReference(x.SeatKey, x.SeatName))
                .ToList();
            await venueWorkflowService.SaveFavoritesAsync(selectedLibrary.LibraryId, selected);
            ApplyFavoriteStates(selected.Select(x => x.SeatKey), syncSelection: false);
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Favorite", $"保存收藏失败：{ex.Message}");
            await notificationService.ShowWarningAsync("保存收藏失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task LoadFavoritesAsync()
    {
        var selectedLibrary = _selectedLibrary?.Invoke();
        if (selectedLibrary is null)
        {
            return;
        }

        try
        {
            var favorites = await venueWorkflowService.GetFavoritesAsync(selectedLibrary.LibraryId);
            ApplyFavoriteStates(favorites.Select(x => x.SeatKey), syncSelection: false);
            if (favorites.Count > 0)
            {
                await notificationService.ShowInfoAsync("收藏已加载", $"已加载 {favorites.Count} 个收藏座位");
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Favorite", $"读取收藏失败：{ex.Message}");
            await notificationService.ShowWarningAsync("读取收藏失败", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearSelectedSeats()
    {
        if (!CanEditGrabConfiguration())
        {
            return;
        }

        _committedSelectedSeatKeys.Clear();
        _draftSelectedSeatKeys.Clear();
        RefreshSelectedSeatsPresentation();
        UpdateDraftSelectionPresentation();
        ApplySelectionToSeatItems(Array.Empty<string>());
    }

    private void OnSeatItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingSeatSelection || e.PropertyName != nameof(SeatItemViewModel.IsSelected))
        {
            return;
        }

        if (IsGrabSeatSelectionOverlayOpen())
        {
            RefreshDraftSelectionFromCurrentItems();
            return;
        }

        RefreshCommittedSelectionFromCurrentItems();
    }

    private void RefreshDraftSelectionFromCurrentItems()
    {
        _draftSelectedSeatKeys.Clear();
        foreach (var seatKey in EnumerateSelectedSeats().Select(seat => seat.SeatKey))
        {
            _draftSelectedSeatKeys.Add(seatKey);
        }

        UpdateDraftSelectionPresentation();
    }

    private void RefreshCommittedSelectionFromCurrentItems()
    {
        _committedSelectedSeatKeys.Clear();
        foreach (var seatKey in EnumerateSelectedSeats().Select(seat => seat.SeatKey))
        {
            _committedSelectedSeatKeys.Add(seatKey);
        }

        RefreshSelectedSeatsPresentation();
    }

    private void UpdateDraftSelectionPresentation()
    {
        OnPropertyChanged(nameof(DraftSelectedSeatCount));
        OnPropertyChanged(nameof(DraftSelectedSeatSummaryText));
    }

    private void ApplySelectionToSeatItems(IEnumerable<string> selectedSeatKeys)
    {
        var seatKeySet = selectedSeatKeys.ToHashSet(StringComparer.Ordinal);
        _isSynchronizingSeatSelection = true;
        try
        {
            foreach (var seat in _allSeats)
            {
                seat.IsSelected = seatKeySet.Contains(seat.SeatKey);
            }
        }
        finally
        {
            _isSynchronizingSeatSelection = false;
        }
    }

    private async Task ApplySeatFilterAsync()
    {
        CancellationTokenSource cts;
        CancellationTokenSource? previousCts;
        lock (_filterGate)
        {
            previousCts = _filteringCts;
            _filteringCts = new CancellationTokenSource();
            cts = _filteringCts;
        }

        previousCts?.Cancel();
        previousCts?.Dispose();

        var filterText = SeatFilterText;
        var showAvailableOnly = ShowAvailableOnly;
        var snapshot = _allSeats
            .Select(seat => new SeatFilterSnapshot(seat, seat.SeatName, seat.IsOccupied))
            .ToArray();

        try
        {
            IsApplyingSeatFilter = true;
            await Task.Yield();

            var filtered = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();

                return snapshot
                    .Select(seat => new SeatFilterResult(
                        seat.ViewModel,
                        ShouldSeatBeVisible(seat.SeatName, seat.IsOccupied, filterText, showAvailableOnly)))
                    .ToArray();
            }, cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            VisibleSeatResultCount = filtered.Count(result => result.IsVisible);
            const int batchSize = 48;
            for (var start = 0; start < filtered.Length; start += batchSize)
            {
                cts.Token.ThrowIfCancellationRequested();

                var count = Math.Min(batchSize, filtered.Length - start);
                for (var offset = 0; offset < count; offset++)
                {
                    var result = filtered[start + offset];
                    result.ViewModel.IsFilterVisible = result.IsVisible;
                }

                if (start + count < filtered.Length)
                {
                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "Library", $"筛选座位失败：{ex.Message}");
        }
        finally
        {
            lock (_filterGate)
            {
                if (ReferenceEquals(_filteringCts, cts))
                {
                    _filteringCts = null;
                }
            }

            IsApplyingSeatFilter = false;
            cts.Dispose();
        }
    }

    private void RefreshSelectedSeatsPresentation()
    {
        SelectedSeats.Clear();
        foreach (var seat in EnumerateSelectedSeats(_committedSelectedSeatKeys))
        {
            SelectedSeats.Add(seat);
        }

        OnPropertyChanged(nameof(SelectedSeatCount));
        OnPropertyChanged(nameof(HasSelectedSeats));
        OnPropertyChanged(nameof(HasNoSelectedSeats));
        OnPropertyChanged(nameof(SelectedSeatSummaryText));
        OnPropertyChanged(nameof(SelectedSeatHintText));
    }

    private IEnumerable<SeatReference> EnumerateSelectedSeats()
    {
        return EnumerateSelectedSeats(_allSeats.Where(x => x.IsSelected).Select(x => x.SeatKey));
    }

    private IEnumerable<SeatReference> EnumerateSelectedSeats(IEnumerable<string> selectedSeatKeys)
    {
        var selectedKeySet = selectedSeatKeys.ToHashSet(StringComparer.Ordinal);

        return _allSeats
            .Where(seat => selectedKeySet.Contains(seat.SeatKey))
            .OrderBy(seat => int.TryParse(seat.SeatName, out var number) ? number : int.MaxValue)
            .ThenBy(seat => seat.SeatName, StringComparer.OrdinalIgnoreCase)
            .Select(seat => new SeatReference(seat.SeatKey, seat.SeatName));
    }

    private bool CanEditGrabConfiguration()
    {
        return _canEditGrabConfiguration?.Invoke() != false;
    }

    private bool IsGrabSeatSelectionOverlayOpen()
    {
        return _isGrabSeatSelectionOverlayOpen?.Invoke() == true;
    }

    private static bool ShouldSeatBeVisible(
        string seatName,
        bool isOccupied,
        string filterText,
        bool showAvailableOnly)
    {
        if (showAvailableOnly && isOccupied)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return seatName.Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SeatFilterSnapshot(
        SeatItemViewModel ViewModel,
        string SeatName,
        bool IsOccupied);

    private sealed record SeatFilterResult(
        SeatItemViewModel ViewModel,
        bool IsVisible);
}
