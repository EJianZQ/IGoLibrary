using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

internal static class AutoReleaseReservationPolicy
{
    public static readonly TimeSpan FailureRetryCooldown = TimeSpan.FromSeconds(5);

    public static bool ShouldCancel(
        ReservationInfo? reservation,
        bool enabled,
        int leadSeconds,
        bool isCancellationInProgress,
        bool isSuppressedByOccupy,
        string? lastFailedReservationToken,
        DateTimeOffset? lastFailedAt,
        DateTimeOffset now)
    {
        if (!enabled ||
            reservation is null ||
            isCancellationInProgress ||
            isSuppressedByOccupy)
        {
            return false;
        }

        var remaining = reservation.ExpirationTime - now;
        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        var normalizedLeadSeconds = AutoReleaseTaskSettings.NormalizeLeadSeconds(leadSeconds);
        if (remaining > TimeSpan.FromSeconds(normalizedLeadSeconds))
        {
            return false;
        }

        return lastFailedReservationToken != reservation.ReservationToken ||
               lastFailedAt is null ||
               now - lastFailedAt.Value >= FailureRetryCooldown;
    }
}
