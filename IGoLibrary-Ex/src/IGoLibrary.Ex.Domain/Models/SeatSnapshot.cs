namespace IGoLibrary.Ex.Domain.Models;

public sealed record SeatSnapshot(
    string SeatKey,
    string SeatName,
    bool IsOccupied,
    int X,
    int Y)
{
    public bool IsAvailable => !IsOccupied;
}
