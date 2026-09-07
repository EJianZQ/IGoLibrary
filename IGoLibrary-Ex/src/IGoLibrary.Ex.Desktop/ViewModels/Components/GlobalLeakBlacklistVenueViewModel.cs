using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed class GlobalLeakBlacklistVenueViewModel(GlobalLeakLibraryTarget target) : ViewModelBase
{
    public GlobalLeakLibraryTarget Target { get; } = target;
    public Dictionary<string, SeatReference> Draft { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<SeatReference> Original { get; private set; } = [];
    public LibraryLayout? Layout { get; set; }
    public IReadOnlyList<SeatLabel> Labels { get; set; } = [];
    public string DisplayText => $"{Target.LibraryName} · {Target.Floor}（黑名单 {Draft.Count}）";
    public bool IsDirty => !Original.OrderBy(static seat => seat.SeatKey, StringComparer.Ordinal)
        .SequenceEqual(Draft.Values.OrderBy(static seat => seat.SeatKey, StringComparer.Ordinal));

    public void Restore(IReadOnlyList<SeatReference> seats)
    {
        Original = seats.ToArray();
        Draft.Clear();
        foreach (var seat in seats) Draft[seat.SeatKey] = seat;
        NotifyCountChanged();
    }

    public void NotifyCountChanged() => OnPropertyChanged(nameof(DisplayText));
}
