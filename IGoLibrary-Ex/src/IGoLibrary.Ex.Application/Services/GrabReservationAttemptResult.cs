using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed record GrabReservationAttemptResult(
    TrackedSeat? ReservedSeat,
    bool HadReservationAttempt,
    bool RateLimitTriggered,
    int NextSeatStartIndex);
