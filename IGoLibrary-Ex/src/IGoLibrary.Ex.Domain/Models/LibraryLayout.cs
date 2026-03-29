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
    public int AvailableSeats => Math.Max(0, TotalSeats - BookedSeats - UsedSeats);
}
