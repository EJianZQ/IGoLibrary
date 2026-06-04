using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

internal sealed record GrabReservationAttemptContext(
    string Cookie,
    GrabSeatPlan Plan,
    int StartIndex,
    Action MarkRequestSent);
