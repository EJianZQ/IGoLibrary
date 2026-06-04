using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record ReservationOperationResult(
    bool Succeeded,
    ReservationInfo? Reservation,
    bool HasSession = true,
    bool RemoteSucceeded = true,
    string? FailureMessage = null);
