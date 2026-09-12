namespace IGoLibrary.Ex.Domain.Models;

public sealed record SeatSnapshot(
    string SeatKey,
    string SeatName,
    bool IsOccupied,
    int X,
    int Y)
{
    public int? SeatStatus { get; init; }
    public bool IsAvailable => !IsOccupied;
}
