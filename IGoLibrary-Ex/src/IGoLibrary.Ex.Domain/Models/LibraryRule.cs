namespace IGoLibrary.Ex.Domain.Models;

public sealed record LibraryRule(
    int LibraryId,
    string AdvanceBooking,
    string SeatTtlMinutes,
    string HoldTtlMinutes,
    string RenewTimeMinutes,
    string HoldReasonJson,
    string? CloseStartDate,
    string? CloseEndDate,
    long OpenTime,
    string OpenTimeText,
    long CloseTime,
    string CloseTimeText,
    int ValidateTime);
