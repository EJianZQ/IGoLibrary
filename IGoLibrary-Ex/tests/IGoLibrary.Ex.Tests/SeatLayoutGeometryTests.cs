using Avalonia;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class SeatLayoutGeometryTests
{
    [Fact]
    public void Sample_ProjectsSeatsDesksPillarsAndDirectionsWithoutTransposingOrCompacting()
    {
        var projection = SeatLayoutProjector.Project(SeatLayoutTestData.Sample());
        Assert.True(projection.CanShowMap);
        Assert.Equal(816, projection.Items.Count);
        ProjectedLayoutItem Seat(string name) => projection.Items.Single(item => item.SeatIndex is not null && item.Item.Name == name);
        Assert.Equal(58, Seat("210").Left - Seat("209").Left);
        Assert.Equal(Seat("209").Left, Seat("212").Left);
        Assert.Equal(116, Seat("212").Top - Seat("209").Top);
        Assert.Equal(Seat("210").Left, Seat("211").Left);
        Assert.Contains(projection.Items, item => item.Item.Type == 2 && item.Item.X == 27 && item.Item.Y == 16);
        Assert.Equal(58, projection.Items.Where(item => item.Item.Name == "柱").Min(item => item.Left));
        Assert.True(projection.Items.Single(item => item.Item.Name == "西").Top < Seat("209").Top);
        Assert.Equal(32 * 58, projection.Width);
        Assert.Equal(102 * 58, projection.Height);
    }

    [Fact]
    public void LegacyCoordinatesAndSmallDeclaredBoundary_UseActualSparseBounds()
    {
        var layout = SeatLayoutTestData.Small() with { MaxX = 1, MaxY = 1 };
        var projection = SeatLayoutProjector.Project(layout);
        Assert.True(projection.BoundaryExpanded);
        Assert.Equal(5, projection.Items.Count);
        Assert.True(projection.Height > 40 * 58);
        Assert.True(SeatLayoutProjector.Project(layout with { LayoutItems = [] }).CanShowMap);
    }

    [Theory]
    [InlineData("duplicate-key")]
    [InlineData("overlap")]
    [InlineData("span")]
    [InlineData("overflow")]
    [InlineData("count")]
    public void AmbiguousOrExcessiveMaps_FallBackWithoutMutatingSeats(string scenario)
    {
        IReadOnlyList<SeatSnapshot> seats = scenario switch
        {
            "duplicate-key" => [new("a", "1", false, 0, 0), new("a", "2", true, 1, 0)],
            "overlap" => [new("a", "1", false, 0, 0), new("b", "2", true, 0, 0)],
            "span" => [new("a", "1", false, 0, 0), new("b", "2", true, 4096, 0)],
            "overflow" => [new("a", "1", false, int.MinValue, 0), new("b", "2", true, int.MaxValue, 0)],
            _ => Enumerable.Range(0, 10001).Select(i => new SeatSnapshot($"k{i}", $"{i}", false, i % 100, i / 100)).ToArray()
        };
        var layout = SeatLayoutTestData.Small() with { Seats = seats, LayoutItems = [] };
        var projection = SeatLayoutProjector.Project(layout);
        Assert.False(projection.CanShowMap);
        Assert.NotEmpty(projection.FallbackReason);
        Assert.Empty(projection.Items);
        Assert.Same(seats, layout.Seats);
    }

    [Fact]
    public void FacilityOnlyAndEmptyMaps_AreDistinct()
    {
        var facility = SeatLayoutProjector.Project(SeatLayoutTestData.Small() with { Seats = [] });
        Assert.True(facility.CanShowMap);
        Assert.Equal(2, facility.Items.Count);
        Assert.False(SeatLayoutProjector.Project(SeatLayoutTestData.Small() with { Seats = [], LayoutItems = [] }).CanShowMap);
    }

    [Fact]
    public void Viewport_AnchorsZoomFitsLongMapsAndClampsOffsets()
    {
        var offset = SeatMapViewport.ZoomAt(new Vector(100, 200), new Point(50, 50), 1, 2);
        Assert.Equal(new Vector(250, 450), offset);
        Assert.Equal(new Vector(50, 150), SeatMapViewport.Center(new Point(100, 200), new Size(100, 100), 1));
        Assert.Equal(0.5, SeatMapViewport.Fit(new Size(1000, 10000), new Size(500, 500), false));
        Assert.Equal(0.05, SeatMapViewport.Fit(new Size(1000, 10000), new Size(500, 500), true));
        Assert.Equal(new Vector(0, 500), SeatMapViewport.Clamp(new Vector(-10, 10000), new Size(1000, 1000), new Size(500, 500), 1));
    }

    [Theory]
    [InlineData(1000, 10000, 500, 500)]
    [InlineData(10000, 1000, 500, 500)]
    [InlineData(237684, 237684, 100, 100)]
    public void ZoomRangeIncludesWholeMap_ForTallWideAndMaximumSpanLayouts(double width, double height, double viewportWidth, double viewportHeight)
    {
        var content = new Size(width, height);
        var viewport = new Size(viewportWidth, viewportHeight);
        var fit = SeatMapViewport.Fit(content, viewport, true);
        Assert.Equal(fit, SeatMapViewport.ClampZoom(fit / 1.2, content, viewport, fit));
        Assert.Equal(fit * 1.2, SeatMapViewport.ClampZoom(fit * 1.2, content, viewport, fit));
        Assert.Equal(fit, SeatMapViewport.ClampZoom(0.00001, content, viewport, 1));
    }

    [Fact]
    public void ZoomRangePreservesCurrentScaleWhenViewportGrows_AndKeepsNormalLimits()
    {
        var content = new Size(1000, 10000);
        Assert.Equal(0.05, SeatMapViewport.ClampZoom(0.04, content, new Size(1000, 1000), 0.05));
        Assert.Equal(0.06, SeatMapViewport.ClampZoom(0.06, content, new Size(1000, 1000), 0.05));
        Assert.Equal(0.1, SeatMapViewport.ClampZoom(0.01, new Size(1000, 1000), new Size(500, 500), 1));
        Assert.Equal(3, SeatMapViewport.ClampZoom(4, content, new Size(500, 500), 1));
    }
}
