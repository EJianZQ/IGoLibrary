using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

public sealed class ToastWindowTests
{
    [Fact]
    public void CalculateLifetimeProgress_ReturnsFullProgress_AtStart()
    {
        var progress = ToastWindow.CalculateLifetimeProgress(TimeSpan.Zero, TimeSpan.FromSeconds(5.5));

        Assert.Equal(1, progress);
    }

    [Fact]
    public void CalculateLifetimeProgress_ReturnsHalfProgress_HalfwayThroughLifetime()
    {
        var progress = ToastWindow.CalculateLifetimeProgress(TimeSpan.FromSeconds(2.75), TimeSpan.FromSeconds(5.5));

        Assert.Equal(0.5, progress, precision: 3);
    }

    [Fact]
    public void CalculateLifetimeProgress_ClampsToZero_WhenElapsedExceedsLifetime()
    {
        var progress = ToastWindow.CalculateLifetimeProgress(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(5.5));

        Assert.Equal(0, progress);
    }
}
