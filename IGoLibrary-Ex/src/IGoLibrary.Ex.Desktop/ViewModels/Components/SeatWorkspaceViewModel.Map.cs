using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SeatWorkspaceViewModel
{
    private readonly List<SeatItemViewModel> _observedSeats = [];
    private bool _synchronizingDuplicates;
    private long _layoutVersion;
    private string? _lastDiagnostic;
    public int? LibraryId { get; private set; }
    public LibraryLayout? SourceLayout { get; private set; }
    public SeatLayoutProjection Projection { get; private set; } = new(0, 0, [], string.Empty, 0, false);
    public event EventHandler? LocationRequested;
    public ObservableCollection<SeatItemViewModel> LocateResults { get; } = [];
    [ObservableProperty] private bool isListMode;
    [ObservableProperty] private string locateText = string.Empty;
    [ObservableProperty] private SeatItemViewModel? locatedSeat;
    [ObservableProperty] private string locationMessage = string.Empty;
    public bool IsMapMode => !IsListMode && Projection.CanShowMap;
    public bool CanShowMap => Projection.CanShowMap;
    public string LayoutNotice => Projection.FallbackReason;
    public bool HasLayoutNotice => LayoutNotice.Length > 0;
    public bool HasLocateResults => LocateResults.Count > 0;
    public string LocateHint => LocatedSeat is { IsFilterVisible: false }
        ? "定位座位不符合当前筛选条件，暂不可选择；可清除筛选后操作" : LocationMessage;

    public void NotifyOpened() => _logger.LogInformation(
        "已打开选座工作区。场馆标识={LibraryId}，座位数量={SeatCount}，列表模式={IsListMode}。",
        LibraryId, Seats.Count, IsListMode);

    public void ApplyLayout(LibraryLayout layout)
    {
        var started = Stopwatch.GetTimestamp();
        var changedVenue = LibraryId != layout.LibraryId;
        var wasFallback = SourceLayout is not null && !Projection.CanShowMap;
        UnsubscribeSeats();
        LibraryId = layout.LibraryId;
        SourceLayout = layout;
        Projection = SeatLayoutProjector.Project(layout);
        ++_layoutVersion;
        foreach (var seat in Seats)
        {
            _observedSeats.Add(seat);
            seat.PropertyChanged += OnWorkspaceSeatChanged;
        }
        ResetLocation();
        if (changedVenue || wasFallback) IsListMode = _preferredListMode;
        if (!Projection.CanShowMap) IsListMode = true;
        OnPropertyChanged(nameof(LibraryId));
        OnPropertyChanged(nameof(Projection));
        NotifyMapPresentation();
        NotifyLayoutChanged();
        _logger.LogInformation(new EventId(3400, "SeatLayoutReady"),
            "座位工作区已准备。场馆标识={LibraryId}，版本={LayoutVersion}，元素数={ElementCount}，座位数={SeatCount}，列表模式={IsListMode}，耗时毫秒={ElapsedMs}。",
            LibraryId, _layoutVersion, layout.LayoutItems.Count, Seats.Count, IsListMode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var diagnostic = $"{LibraryId}:{layout.InvalidLayoutItemCount}:{Projection.UnknownTypeCount}:{Projection.BoundaryExpanded}:{LayoutNotice}";
        if (diagnostic != _lastDiagnostic && (layout.InvalidLayoutItemCount > 0 || Projection.UnknownTypeCount > 0 ||
            Projection.BoundaryExpanded || HasLayoutNotice))
        {
            _logger.LogWarning(new EventId(3401, "SeatLayoutAdjusted"),
                "布局展示兼容处理。场馆标识={LibraryId}，无效元素数={InvalidCount}，中性设施数={UnknownCount}，边界扩展={BoundaryExpanded}，降级原因={FallbackReason}。",
                LibraryId, layout.InvalidLayoutItemCount, Projection.UnknownTypeCount, Projection.BoundaryExpanded, LayoutNotice);
            if (HasLayoutNotice) activityLogService.Write(LogEntryKind.Warning, "Library", LayoutNotice);
        }
        _lastDiagnostic = diagnostic;
    }

    partial void OnIsListModeChanged(bool value)
    {
        if (!value && SourceLayout is not null && !Projection.CanShowMap) { IsListMode = true; return; }
        OnPropertyChanged(nameof(IsMapMode));
        OnPropertyChanged(nameof(SelectedViewIndex));
        _ = RefreshAsync();
        _logger.LogDebug("已切换座位视图。场馆标识={LibraryId}，列表模式={IsListMode}。", LibraryId, value);
    }
    partial void OnLocateTextChanged(string value) => ResetLocation();
    partial void OnLocationMessageChanged(string value) => OnPropertyChanged(nameof(LocateHint));
    partial void OnLocatedSeatChanged(SeatItemViewModel? oldValue, SeatItemViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsLocated = false;
        if (newValue is not null)
        {
            newValue.IsLocated = true;
            LocationMessage = "已定位，仅高亮，不改变勾选";
            LocationRequested?.Invoke(this, EventArgs.Empty);
        }
        OnPropertyChanged(nameof(LocateHint));
    }

    [RelayCommand]
    private void Locate()
    {
        ResetLocation();
        var query = LocateText.Trim();
        if (query.Length == 0) { LocationMessage = "请输入座位号"; return; }
        var exact = Seats.Where(seat => string.Equals(seat.SeatName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        var results = exact.Length > 0 ? exact : Seats.Where(seat => seat.SeatName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var seat in results) LocateResults.Add(seat);
        OnPropertyChanged(nameof(HasLocateResults));
        LocationMessage = results.Length == 0 ? "未找到对应座位" : "请选择要定位的座位";
        if (results.Length == 1) LocatedSeat = results[0];
        _logger.LogDebug("座位定位检索完成。场馆标识={LibraryId}，结果数量={MatchCount}。", LibraryId, results.Length);
    }

    private void ResetLocation()
    {
        LocatedSeat = null;
        LocateResults.Clear();
        LocationMessage = string.Empty;
        OnPropertyChanged(nameof(HasLocateResults));
    }
    private void NotifyMapPresentation()
    {
        OnPropertyChanged(nameof(IsMapMode));
        OnPropertyChanged(nameof(CanShowMap));
        OnPropertyChanged(nameof(LayoutNotice));
        OnPropertyChanged(nameof(HasLayoutNotice));
    }
    private void OnWorkspaceSeatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeatItemViewModel.IsFilterVisible)) OnPropertyChanged(nameof(LocateHint));
        if (_synchronizingDuplicates || e.PropertyName != nameof(SeatItemViewModel.IsSelected) || sender is not SeatItemViewModel source) return;
        _synchronizingDuplicates = true;
        try
        {
            foreach (var seat in Seats.Where(seat => seat.SeatKey == source.SeatKey && seat != source)) seat.IsSelected = source.IsSelected;
        }
        finally { _synchronizingDuplicates = false; }
    }
    private void UnsubscribeSeats()
    {
        foreach (var seat in _observedSeats) seat.PropertyChanged -= OnWorkspaceSeatChanged;
        _observedSeats.Clear();
    }
    private void ClearMap()
    {
        UnsubscribeSeats();
        ResetLocation();
        LibraryId = null;
        SourceLayout = null;
        _lastDiagnostic = null;
        Projection = new(0, 0, [], string.Empty, 0, false);
        OnPropertyChanged(nameof(Projection));
        NotifyMapPresentation();
    }
}
