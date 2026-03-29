namespace IGoLibrary.Ex.Domain.Models;

public sealed record LibrarySummary(
    int LibraryId,
    string Name,
    string Floor,
    bool IsOpen,
    int SeatsTotal = 0,
    int SeatsUsed = 0,
    int SeatsBooking = 0);
