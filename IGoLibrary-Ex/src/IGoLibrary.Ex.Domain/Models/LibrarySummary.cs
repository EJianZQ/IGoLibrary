namespace IGoLibrary.Ex.Domain.Models;

public sealed record LibrarySummary(
    int LibraryId,
    string Name,
    string Floor,
    bool IsOpen,
    int TotalSeats = 0,
    int UsedSeats = 0,
    int BookedSeats = 0);
