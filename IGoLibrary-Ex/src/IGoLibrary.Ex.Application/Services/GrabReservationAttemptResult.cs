using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed record GrabReservationAttemptResult(
    SeatReference? ReservedSeat,
    bool HadReservationAttempt,
    bool RateLimitTriggered,
    int NextSeatStartIndex,
    LibraryLayout? LatestLayout = null,
    string? ReservedLibraryName = null);
