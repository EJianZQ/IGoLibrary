using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SeatItemViewModel : ObservableObject
{
    private readonly Func<SeatItemViewModel, Task>? _editLabelAsync;
    private readonly Func<SeatItemViewModel, Task>? _deleteLabelAsync;

    public SeatItemViewModel(
        string seatKey,
        string seatName,
        bool isOccupied,
        Func<SeatItemViewModel, Task>? editLabelAsync = null,
        Func<SeatItemViewModel, Task>? deleteLabelAsync = null)
    {
        SeatKey = seatKey;
        SeatName = seatName;
        IsOccupied = isOccupied;
        _editLabelAsync = editLabelAsync;
        _deleteLabelAsync = deleteLabelAsync;
    }

    public string SeatKey { get; }

    public string SeatName { get; }

    public bool IsOccupied { get; set; }

    public bool IsAvailable => !IsOccupied;

    public bool IsUnavailable => IsOccupied;

    public string StatusText => IsOccupied ? "有人" : "无人";

    public int? SeatStatus { get; init; }
    public string LayoutStatusText => SeatStatus switch
    {
        1 => "空闲",
        2 => "平台已选",
        3 => "有人",
        4 => "暂离",
        _ => $"{StatusText}（{(SeatStatus is null ? "无详细状态" : $"未知状态 {SeatStatus}")}）"
    };
    public string LocationDisplayText => $"{SeatName} · {SeatKey}";
    public string LayoutToolTipText => $"座位 {SeatName}\n标识：{SeatKey}\n状态：{LayoutStatusText}\n占用标记：{StatusText}" +
        (IsFavorite ? "\n已收藏" : string.Empty) +
        (HasLabel ? $"\n标签：{LabelText}" : string.Empty) +
        (IsFilterVisible ? string.Empty : "\n不符合当前筛选条件");
    [ObservableProperty] private bool isLocated;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutToolTipText))]
    private bool isFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutToolTipText))]
    private bool isFilterVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayoutToolTipText))]
    private string? labelText;

    public bool HasLabel => !string.IsNullOrWhiteSpace(LabelText);

    public bool SupportsLabelEditing => _editLabelAsync is not null;

    public string LabelMenuHeader => HasLabel ? "编辑标签" : "添加标签";

    partial void OnLabelTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLabel));
        OnPropertyChanged(nameof(LabelMenuHeader));
    }

    [RelayCommand]
    private Task EditLabelAsync()
    {
        return _editLabelAsync?.Invoke(this) ?? Task.CompletedTask;
    }

    [RelayCommand]
    private Task DeleteLabelAsync()
    {
        return HasLabel
            ? _deleteLabelAsync?.Invoke(this) ?? Task.CompletedTask
            : Task.CompletedTask;
    }
}
