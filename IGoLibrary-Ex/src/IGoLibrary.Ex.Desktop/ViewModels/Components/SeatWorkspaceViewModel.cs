using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

/// <summary>独立的座位展示与筛选工作区，不提交选择或写入持久化。</summary>
public sealed partial class SeatWorkspaceViewModel(IActivityLogService activityLogService) : ViewModelBase, IDisposable
{
    private readonly object _filterGate = new();
    private CancellationTokenSource? _filteringCts;
    public ObservableCollection<SeatItemViewModel> Seats { get; } = [];
    public ObservableCollection<SeatItemViewModel> VisibleSeats => Seats;
    [ObservableProperty] private string seatFilterText = string.Empty;
    [ObservableProperty] private bool showAvailableOnly;
    [ObservableProperty] private bool isApplyingSeatFilter;
    [ObservableProperty] private int visibleSeatResultCount;
    public bool HasSeatLayout => Seats.Count > 0;
    public bool HasNoSeatLayout => !HasSeatLayout;
    public bool ShowSeatFilterEmptyState => HasSeatLayout && VisibleSeatResultCount == 0;
    partial void OnSeatFilterTextChanged(string value) => _ = RefreshAsync();
    partial void OnShowAvailableOnlyChanged(bool value) => _ = RefreshAsync();
    partial void OnVisibleSeatResultCountChanged(int value) => OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
    public void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(HasSeatLayout));
        OnPropertyChanged(nameof(HasNoSeatLayout));
        OnPropertyChanged(nameof(ShowSeatFilterEmptyState));
    }
    public void CancelFiltering()
    {
        lock (_filterGate)
        {
            _filteringCts?.Cancel();
            _filteringCts = null;
            IsApplyingSeatFilter = false;
        }
    }
    public void Clear()
    {
        CancelFiltering();
        Seats.Clear();
        VisibleSeatResultCount = 0;
        NotifyLayoutChanged();
    }
    public void Dispose() => CancelFiltering();

    public async Task RefreshAsync()
    {
        CancellationTokenSource cts;
        CancellationToken token;
        lock (_filterGate)
        {
            _filteringCts?.Cancel();
            _filteringCts = new CancellationTokenSource();
            cts = _filteringCts;
            token = cts.Token;
        }

        var filterText = SeatFilterText;
        var showAvailableOnly = ShowAvailableOnly;
        var snapshot = Seats
            .Select(seat => new SeatFilterSnapshot(seat, seat.SeatName, seat.LabelText, seat.IsOccupied))
            .ToArray();

        NotifyLayoutChanged();
        try
        {
            IsApplyingSeatFilter = true;
            await Task.Yield();

            var filtered = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                return snapshot
                    .Select(seat => new SeatFilterResult(
                        seat.ViewModel,
                        ShouldSeatBeVisible(
                            seat.SeatName,
                            seat.LabelText,
                            seat.IsOccupied,
                            filterText,
                            showAvailableOnly)))
                    .ToArray();
            }, token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            VisibleSeatResultCount = filtered.Count(result => result.IsVisible);
            const int batchSize = 48;
            for (var start = 0; start < filtered.Length; start += batchSize)
            {
                token.ThrowIfCancellationRequested();

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
            activityLogService.Write(LogEntryKind.Error, "Library", $"筛选座位失败：{ex.Message}", ex);
        }
        finally
        {
            lock (_filterGate)
            {
                if (ReferenceEquals(_filteringCts, cts))
                {
                    _filteringCts = null;
                    IsApplyingSeatFilter = false;
                }
            }

            cts.Dispose();
        }
    }

    private static bool ShouldSeatBeVisible(
        string seatName,
        string? labelText,
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

        var normalizedFilterText = filterText.Trim();
        return seatName.Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase) ||
               labelText?.Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record SeatFilterSnapshot(
        SeatItemViewModel ViewModel,
        string SeatName,
        string? LabelText,
        bool IsOccupied);

    private sealed record SeatFilterResult(
        SeatItemViewModel ViewModel,
        bool IsVisible);
}
