namespace IGoLibrary.Ex.Domain.Models;

public sealed record LibraryLayout(
    int LibraryId,
    string Name,
    string Floor,
    bool IsOpen,
    int TotalSeats,
    int BookedSeats,
    int UsedSeats,
    IReadOnlyList<SeatSnapshot> Seats)
{
    public int? MaxX { get; init; }
    public int? MaxY { get; init; }
    public IReadOnlyList<LibraryLayoutItem> LayoutItems { get; init; } = [];
    public int InvalidLayoutItemCount { get; init; }
    public int AvailableSeats => Math.Max(0, TotalSeats - BookedSeats - UsedSeats);
}
