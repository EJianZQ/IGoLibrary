using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatMarqueeTests
{
    [Fact]
    public void Geometry_NormalizesReverseDragAndIgnoresEmptyRectangles()
    {
        var rect = SeatMarqueeGeometry.Normalize(new Point(120, 90), new Point(20, 10));
        Assert.Equal(new Rect(20, 10, 100, 80), rect);
        Assert.True(SeatMarqueeGeometry.Intersects(rect, new Rect(0, 0, 30, 30)));
        Assert.False(SeatMarqueeGeometry.Intersects(rect, new Rect(200, 200, 52, 52)));
        Assert.False(SeatMarqueeGeometry.Intersects(new Rect(10, 10, 0, 0), new Rect(0, 0, 52, 52)));
        // 水平拖拽产生零高矩形，按线段命中同一行的座位。
        Assert.True(SeatMarqueeGeometry.Intersects(new Rect(20, 30, 120, 0), new Rect(60, 0, 52, 52)));
    }

    [Fact]
    public void Geometry_RequiresThresholdBeforeStartingMarquee()
    {
        var origin = new Point(10, 10);
        Assert.False(SeatMarqueeGeometry.HasReachedThreshold(origin, new Point(14, 12)));
        Assert.True(SeatMarqueeGeometry.HasReachedThreshold(origin, new Point(20, 10)));
    }

    [AvaloniaFact]
    public void MapDrag_SelectsEverySeatUnderTheMarquee()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Small());
        var (view, window, map) = Show(workspace);
        Assert.NotNull(map);
        try
        {
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            var tiles = SeatTiles(view);
            var first = tiles[0];
            var second = tiles[1];
            var from = Center(first, window);
            var to = Center(second, window);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(new Point((from.X + to.X) / 2, from.Y), RawInputModifiers.LeftMouseButton);
            Assert.True(map.IsMarqueeActive);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Assert.True(map.MarqueePreviewCount >= 2);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(workspace.Seats[0].IsSelected);
            Assert.True(workspace.Seats[1].IsSelected);
            Assert.False(workspace.Seats[0].IsMarqueePreview);
            Assert.False(workspace.Seats[1].IsMarqueePreview);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MapDrag_KeepsAlreadySelectedSeatsAndSkipsFilteredSeats()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Small());
        var (view, window, map) = Show(workspace);
        Assert.NotNull(map);
        try
        {
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            workspace.Seats[2].IsSelected = true;
            workspace.Seats[1].IsFilterVisible = false;
            var tiles = SeatTiles(view);
            var from = Center(tiles[0], window);
            var to = Center(tiles[1], window);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(workspace.Seats[0].IsSelected);
            Assert.False(workspace.Seats[1].IsSelected);
            Assert.True(workspace.Seats[2].IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MapClick_WithoutDragStillTogglesOnlyThatSeat()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Small());
        var (view, window, map) = Show(workspace);
        Assert.NotNull(map);
        try
        {
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            var point = Center(SeatTiles(view)[0], window);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(map.IsMarqueeActive);
            Assert.True(workspace.Seats[0].IsSelected);
            Assert.False(workspace.Seats[1].IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MapDragOnEmptyArea_AlsoStartsMarquee()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Small());
        var (view, window, map) = Show(workspace);
        Assert.NotNull(map);
        try
        {
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            var seatPoint = Center(SeatTiles(view)[0], window);
            var empty = map.TranslatePoint(new Point(120, 400), window)!.Value;

            window.MouseDown(empty, MouseButton.Left);
            window.MouseMove(new Point((empty.X + seatPoint.X) / 2, (empty.Y + seatPoint.Y) / 2), RawInputModifiers.LeftMouseButton);
            Assert.True(map.IsMarqueeActive);
            window.MouseMove(seatPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(seatPoint, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(workspace.Seats[0].IsSelected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ListDrag_SelectsEverySeatUnderTheMarquee()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Sample());
        workspace.SelectedViewIndex = 1;
        var (view, window, _) = Show(workspace);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(workspace.IsListMode);
            var tiles = SeatTiles(view);
            var from = Center(tiles[0], window);
            var to = Center(tiles[2], window);

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            Assert.True(view.IsListMarqueeActive);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            Assert.True(view.ListMarqueePreviewCount >= 2);
            window.MouseUp(to, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(workspace.Seats.Count(seat => seat.IsSelected) >= 2);
        }
        finally
        {
            window.Close();
        }
    }

    private static (SeatWorkspaceView View, Window Window, SeatMapView? Map) Show(SeatWorkspaceViewModel workspace)
    {
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var map = view.GetLogicalDescendants().OfType<SeatMapView>().SingleOrDefault();
        return (view, window, map);
    }

    private static SeatTile[] SeatTiles(SeatWorkspaceView view) =>
        view.GetVisualDescendants()
            .OfType<SeatTile>()
            .Where(tile => tile.DataContext is SeatItemViewModel { IsFilterVisible: true })
            .OrderBy(tile => tile.DataContext is SeatItemViewModel seat ? seat.SeatName : string.Empty)
            .ToArray();

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
}
