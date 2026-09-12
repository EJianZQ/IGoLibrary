using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Controls;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatMapBadgeTests
{
    [AvaloniaTheory]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.46)]
    [InlineData(0.47)]
    [InlineData(0.48)]
    [InlineData(1)]
    [InlineData(1.33)]
    [InlineData(3)]
    public void ZoomedAnnotationsRemainInsideSeat_WithoutCoveringNumber(double zoom)
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var seat = workspace.Seats[0];
        seat.IsFavorite = true;
        seat.LabelText = "靠窗";
        seat.IsSelected = true;
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            map.SetZoom(zoom);
            Dispatcher.UIThread.RunJobs();
            var tile = map.GetLogicalDescendants().OfType<SeatTile>().First();
            var favorite = tile.FindControl<Border>("FavoriteBadge")!;
            var label = tile.FindControl<Border>("LabelBadge")!;
            var number = tile.FindControl<Viewbox>("SeatNumberBox")!;
            Assert.True(favorite.IsVisible && label.IsVisible);
            Assert.True(tile.FindControl<Border>("SelectionBadge")!.IsVisible);
            Assert.InRange(favorite.Bounds.Width * map.Zoom, zoom >= 0.47 ? 12 : 1, zoom >= 0.47 ? 18.01 : 5.01);
            Assert.True(favorite.Bounds.Right <= label.Bounds.Left + 0.01);
            Assert.True(label.Bounds.Right <= 52.01);
            Assert.True(favorite.Bounds.Bottom <= 52.01);
            var numberTop = number.TranslatePoint(default, tile)!.Value.Y;
            Assert.True(numberTop >= 4.99);
            Assert.True(numberTop + number.Bounds.Height <= favorite.Bounds.Top + 0.01);
            Assert.Equal(zoom >= 0.47, tile.FindControl<Avalonia.Controls.Shapes.Path>("FavoriteGlyph")!.IsVisible);
            Assert.Equal(zoom >= 0.47, tile.FindControl<Avalonia.Controls.Shapes.Path>("LabelGlyph")!.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BadgeClickSelectsSeat_AndLiveMetadataAndFilteringStillWork()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var seat = workspace.Seats[0];
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            map.SetZoom(0.48);
            Dispatcher.UIThread.RunJobs();
            var tile = map.GetLogicalDescendants().OfType<SeatTile>().First();
            var badge = tile.FindControl<Border>("FavoriteBadge")!;
            Assert.False(badge.IsVisible);
            seat.IsFavorite = true;
            seat.LabelText = "靠窗";
            Dispatcher.UIThread.RunJobs();
            Assert.True(badge.IsVisible);
            Assert.Contains("已收藏", seat.LayoutToolTipText);
            Assert.Contains("靠窗", seat.LayoutToolTipText);
            var point = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window)!.Value;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(seat.IsSelected);
            workspace.SeatFilterText = "不匹配任何座位";
            await workspace.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(seat.IsSelected);
            Assert.False(seat.IsFilterVisible);
            seat.IsFavorite = false;
            seat.LabelText = null;
            Assert.False(badge.IsVisible);
            Assert.False(tile.FindControl<Border>("LabelBadge")!.IsVisible);
            Assert.DoesNotContain("已收藏", seat.LayoutToolTipText);
        }
        finally { window.Close(); }
    }
}
