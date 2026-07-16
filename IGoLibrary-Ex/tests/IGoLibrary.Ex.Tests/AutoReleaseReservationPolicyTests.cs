using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class AutoReleaseReservationPolicyTests
{
    [Theory]
    [InlineData(false, false, false, 30, false)]
    [InlineData(true, true, false, 30, false)]
    [InlineData(true, false, true, 30, false)]
    [InlineData(true, false, false, -1, false)]
    [InlineData(true, false, false, 61, false)]
    [InlineData(true, false, false, 60, true)]
    public void ShouldCancel_RespectsGuardsAndLeadWindow(
        bool enabled,
        bool isCancellationInProgress,
        bool isSuppressedByOccupy,
        int remainingSeconds,
        bool expected)
    {
        var now = new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(now.AddSeconds(remainingSeconds));

        var shouldCancel = AutoReleaseReservationPolicy.ShouldCancel(
            reservation,
            enabled,
            leadSeconds: 60,
            isCancellationInProgress,
            isSuppressedByOccupy,
            lastFailedReservationToken: null,
            lastFailedAt: null,
            now);

        Assert.Equal(expected, shouldCancel);
    }

    [Fact]
    public void ShouldCancel_ReturnsFalse_WhenReservationIsMissing()
    {
        var now = new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero);

        var shouldCancel = AutoReleaseReservationPolicy.ShouldCancel(
            reservation: null,
            enabled: true,
            leadSeconds: 60,
            isCancellationInProgress: false,
            isSuppressedByOccupy: false,
            lastFailedReservationToken: null,
            lastFailedAt: null,
            now);

        Assert.False(shouldCancel);
    }

    [Fact]
    public void ShouldCancel_SuppressesSameReservationUntilFailureCooldownElapses()
    {
        var now = new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero);
        var reservation = CreateReservation(now.AddSeconds(30));

        var suppressed = AutoReleaseReservationPolicy.ShouldCancel(
            reservation,
            enabled: true,
            leadSeconds: 60,
            isCancellationInProgress: false,
            isSuppressedByOccupy: false,
            lastFailedReservationToken: reservation.ReservationToken,
            lastFailedAt: now.AddSeconds(-4),
            now);
        var retried = AutoReleaseReservationPolicy.ShouldCancel(
            reservation,
            enabled: true,
            leadSeconds: 60,
            isCancellationInProgress: false,
            isSuppressedByOccupy: false,
            lastFailedReservationToken: reservation.ReservationToken,
            lastFailedAt: now.AddSeconds(-5),
            now);

        Assert.False(suppressed);
        Assert.True(retried);
    }

    private static ReservationInfo CreateReservation(DateTimeOffset expirationTime)
    {
        return new ReservationInfo(
            "token-1",
            1,
            "自科阅览区一",
            "seat-1",
            "1",
            expirationTime);
    }
}
