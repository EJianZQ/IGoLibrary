using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatMapWindowTests
{
    [AvaloniaFact]
    public async Task MapClicks_FilteringAndListSwitch_UseTheSameSeatSelection()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            Assert.Equal(5, map.ElementCount);
            map.SetZoom(1);
            map.Fit(true);
            Dispatcher.UIThread.RunJobs();
            var tile = view.GetLogicalDescendants().OfType<SeatTile>().Single(tile => ReferenceEquals(tile.DataContext, workspace.Seats[1]));
            var clickDescription = Click(window, tile);
            Assert.True(workspace.Seats[1].IsSelected, clickDescription);
            workspace.SeatFilterText = "1";
            await workspace.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tile.FindControl<ToggleButton>("SeatToggle")!.IsEnabled);
            Click(window, tile);
            Assert.True(workspace.Seats[1].IsSelected);
            Assert.Equal(5, map.ElementCount);
            workspace.IsListMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(view.GetLogicalDescendants().OfType<SeatMapView>());
            var listTile = view.GetLogicalDescendants().OfType<SeatTile>().Single(tile => ReferenceEquals(tile.DataContext, workspace.Seats[1]));
            Assert.False(listTile.IsVisible);
            workspace.IsListMode = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(map, view.GetLogicalDescendants().OfType<SeatMapView>().Single());
            Assert.True(workspace.Seats[1].IsSelected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DragOverSeat_DoesNotSelect_AndLocateOnlyHighlights()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            var tile = view.GetLogicalDescendants().OfType<SeatTile>().First();
            var point = tile.TranslatePoint(new Point(26, 26), window)!.Value;
            window.MouseDown(point, MouseButton.Middle);
            window.MouseMove(new Point(point.X, point.Y - 80));
            window.MouseUp(new Point(point.X, point.Y - 80), MouseButton.Middle);
            Assert.All(workspace.Seats, seat => Assert.False(seat.IsSelected));
            Assert.True(map.Offset.Y > 0);
            workspace.LocateText = "21";
            workspace.LocateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(map.Offset.Y > 500);
            Assert.True(workspace.Seats[2].IsLocated);
            Assert.All(workspace.Seats, seat => Assert.False(seat.IsSelected));
            Assert.True(map.Zoom >= 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RefreshPreservesViewport_AndVenueSwitchFitsFromTop()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            workspace.LocateText = "21";
            workspace.LocateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var offset = map.Offset;
            var zoom = map.Zoom;
            SeatWorkspaceMapTests.Populate(workspace, SeatLayoutTestData.Small());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(zoom, map.Zoom);
            var scroll = map.GetLogicalDescendants().OfType<ScrollViewer>().Single();
            Assert.InRange(Math.Abs(Math.Min(offset.Y, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)) - map.Offset.Y), 0, 1);
            SeatWorkspaceMapTests.Populate(workspace, SeatLayoutTestData.Small(2));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, map.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1000, 680, false)]
    [InlineData(1188, 840, true)]
    public void SampleFitsViewportInBothThemes_WithAllActualElements(int width, int height, bool dark)
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Sample());
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            Assert.Equal(816, map.ElementCount);
            Assert.Equal(356, map.GetLogicalDescendants().OfType<SeatTile>().Count());
            Assert.True(map.Bounds.Height > 100);
            Assert.True(map.Bounds.Width <= width);
            Assert.True(map.Zoom < 1);
        }
        finally { window.Close(); }
    }
    private static string Click(Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point);
        Assert.True(hit is not null, $"No hit at {point}; tile bounds {control.Bounds}");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return $"Point {point}, hit {hit?.GetType().Name}, ancestors {string.Join('/', (hit as Visual)?.GetVisualAncestors().Select(visual => visual.GetType().Name) ?? [])}, children {string.Join(';', control.GetVisualDescendants().Select(v => $"{v.GetType().Name}:{v.Bounds}"))}";
    }
}
