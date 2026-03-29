using CommunityToolkit.Mvvm.ComponentModel;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SeatItemViewModel(string seatKey, string seatName, bool isOccupied) : ObservableObject
{
    public string SeatKey { get; } = seatKey;

    public string SeatName { get; } = seatName;

    public bool IsOccupied { get; set; } = isOccupied;

    public bool IsAvailable => !IsOccupied;

    public bool IsUnavailable => IsOccupied;

    public string StatusText => IsOccupied ? "有人" : "无人";

    [ObservableProperty]
    private bool isSelected;
}
