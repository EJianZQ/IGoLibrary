using IGoLibrary.Ex.Domain.Helpers;

namespace IGoLibrary.Ex.Tests;

public sealed class ReservationTimeHelperTests
{
    [Fact]
    public void FromUnixSeconds_ReturnsLocalTime()
    {
        const long timestamp = 1_710_000_000;

        var actual = ReservationTimeHelper.FromUnixSeconds(timestamp);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime(), actual);
    }

    [Fact]
    public void ShouldReReserve_ReturnsTrue_WhenExpirationWithinSixtySeconds()
    {
        var now = DateTimeOffset.Now;
        var expiration = now.AddSeconds(45);

        var shouldReReserve = ReservationTimeHelper.ShouldReReserve(expiration, now);

        Assert.True(shouldReReserve);
    }

    [Fact]
    public void ShouldReReserve_ReturnsFalse_WhenExpirationStillFarAway()
    {
        var now = DateTimeOffset.Now;
        var expiration = now.AddSeconds(180);

        var shouldReReserve = ReservationTimeHelper.ShouldReReserve(expiration, now);

        Assert.False(shouldReReserve);
    }
}
