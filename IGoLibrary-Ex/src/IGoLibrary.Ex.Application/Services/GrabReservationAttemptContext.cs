using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed record GrabReservationAttemptContext(
    string Cookie,
    GrabSeatPlan Plan,
    IReadOnlySet<string> TargetSeatKeys,
    int StartIndex,
    Action MarkRequestSent);
