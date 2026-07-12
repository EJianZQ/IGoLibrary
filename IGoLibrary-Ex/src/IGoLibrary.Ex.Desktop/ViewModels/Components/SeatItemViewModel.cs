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

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isFavorite;

    [ObservableProperty]
    private bool isFilterVisible = true;

    [ObservableProperty]
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
