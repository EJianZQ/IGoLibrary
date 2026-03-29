using Avalonia;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class ToastLayoutCalculatorTests
{
    [Fact]
    public void Calculate_PlacesNewestToastAtBottomRight()
    {
        var workingArea = new PixelRect(0, 0, 1920, 1080);
        var sizes = new[]
        {
            new PixelSize(360, 96),
            new PixelSize(360, 104),
            new PixelSize(360, 112)
        };

        var positions = ToastLayoutCalculator.Calculate(workingArea, sizes, margin: 20, spacing: 12);

        Assert.Equal(new PixelPoint(1540, 724), positions[0]);
        Assert.Equal(new PixelPoint(1540, 832), positions[1]);
        Assert.Equal(new PixelPoint(1540, 948), positions[2]);
    }

    [Fact]
    public void Calculate_ReturnsEmpty_WhenNoToastSizesProvided()
    {
        var positions = ToastLayoutCalculator.Calculate(new PixelRect(0, 0, 1440, 900), [], margin: 20, spacing: 12);

        Assert.Empty(positions);
    }
}
