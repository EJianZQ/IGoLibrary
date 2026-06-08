namespace IGoLibrary.Ex.Domain.Models;

public sealed record TomorrowReservationInfo(
    string Day,
    int LibraryId,
    string SeatKey,
    string SeatName,
    bool IsUsed);
