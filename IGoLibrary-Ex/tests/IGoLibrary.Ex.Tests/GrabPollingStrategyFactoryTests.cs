using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabPollingStrategyFactoryTests
{
    [Theory]
    [InlineData(GrabPollingMode.Aggressive, 1, 1)]
    [InlineData(GrabPollingMode.Randomized, 4, 8)]
    [InlineData(GrabPollingMode.Relaxed, 5, 5)]
    public void FromMode_ReturnsExpectedDelayWindow(GrabPollingMode mode, int minDelaySeconds, int maxDelaySeconds)
    {
        var strategy = GrabPollingStrategyFactory.FromMode(mode);

        Assert.Equal(TimeSpan.FromSeconds(minDelaySeconds), strategy.MinimumDelay);
        Assert.Equal(TimeSpan.FromSeconds(maxDelaySeconds), strategy.MaximumDelay);
        Assert.Equal(50, strategy.CooldownEveryCycles);
        Assert.Equal(TimeSpan.FromSeconds(5), strategy.CooldownMinimum);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.CooldownMaximum);
    }
}
