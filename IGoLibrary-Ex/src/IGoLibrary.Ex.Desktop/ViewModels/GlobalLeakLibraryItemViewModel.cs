using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class GlobalLeakLibraryItemViewModel(LibrarySummary library) : ViewModelBase
{
    public LibrarySummary Library { get; } = library;

    public int LibraryId => Library.LibraryId;

    public string LibraryName => Library.Name;

    public string Floor => string.IsNullOrWhiteSpace(Library.Floor) ? "未标注楼层" : Library.Floor;

    public int AvailableSeats => Math.Max(0, Library.TotalSeats - Library.UsedSeats - Library.BookedSeats);

    public string SeatSummary => $"{AvailableSeats} / {Math.Max(0, Library.TotalSeats)} 空座";

    public bool IsOpen => Library.IsOpen;

    public string OpenStatusText => Library.IsOpen ? "开放中" : "已闭馆";

    [ObservableProperty]
    private bool isSelected;
}
