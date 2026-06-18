namespace IGoLibrary.Ex.Domain.Models;

public sealed record ReservationInfo(
    string ReservationToken,
    int LibraryId,
    string LibraryName,
    string SeatKey,
    string SeatName,
    DateTimeOffset ExpirationTime,
    int? Status = null,
    DateTimeOffset? ValidateTime = null,
    DateTimeOffset? HoldTime = null)
{
    public bool IsCheckedIn => HoldTime.HasValue;
}
