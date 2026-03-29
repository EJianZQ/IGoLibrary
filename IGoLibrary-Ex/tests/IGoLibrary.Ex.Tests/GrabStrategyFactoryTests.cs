using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabStrategyFactoryTests
{
    [Theory]
    [InlineData(GrabMode.Aggressive, 1, 1)]
    [InlineData(GrabMode.Randomized, 4, 8)]
    [InlineData(GrabMode.Relaxed, 5, 5)]
    public void FromMode_ReturnsExpectedDelayWindow(GrabMode mode, int minDelaySeconds, int maxDelaySeconds)
    {
        var strategy = GrabStrategyFactory.FromMode(mode);

        Assert.Equal(TimeSpan.FromSeconds(minDelaySeconds), strategy.MinimumDelay);
        Assert.Equal(TimeSpan.FromSeconds(maxDelaySeconds), strategy.MaximumDelay);
        Assert.Equal(50, strategy.CooldownEveryCycles);
        Assert.Equal(TimeSpan.FromSeconds(5), strategy.CooldownMinimum);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.CooldownMaximum);
    }
}
