using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatMapInputTests
{
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.None)]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    public void WholeMapBelowTenPercent_ZoomOutNeverEnlarges_AndZoomInRemainsGradual(RawInputModifiers modifiers)
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace(SeatLayoutTestData.Sample());
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            map.Fit(true);
            Dispatcher.UIThread.RunJobs();
            var wholeMapZoom = map.Zoom;
            Assert.InRange(wholeMapZoom, 0.001, 0.099);
            workspace.Seats[0].IsSelected = true;

            void Zoom(bool zoomIn)
            {
                if (modifiers == RawInputModifiers.None)
                {
                    var button = view.GetLogicalDescendants().OfType<Button>()
                        .Single(button => Equals(button.Content, zoomIn ? "＋" : "−"));
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
                else
                {
                    var point = map.TranslatePoint(new Point(100, 100), window)!.Value;
                    window.MouseWheel(point, new Vector(0, zoomIn ? 1 : -1), modifiers);
                }
                Dispatcher.UIThread.RunJobs();
            }

            Zoom(false);
            Assert.Equal(wholeMapZoom, map.Zoom);
            Zoom(true);
            var factor = modifiers == RawInputModifiers.None ? 1.2 : 1.15;
            Assert.Equal(wholeMapZoom * factor, map.Zoom, 8);
            Zoom(false);
            Assert.Equal(wholeMapZoom, map.Zoom, 8);
            Assert.True(workspace.Seats[0].IsSelected);
            map.SetZoom(1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, map.Zoom);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardSelectionAndModifiedWheel_WorkWithoutChangingOtherSeats()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var map = view.GetLogicalDescendants().OfType<SeatMapView>().Single();
            var tile = view.GetLogicalDescendants().OfType<SeatTile>().First();
            tile.FindControl<ToggleButton>("SeatToggle")!.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(workspace.Seats[0].IsSelected);
            Assert.False(workspace.Seats[1].IsSelected);
            var zoom = map.Zoom;
            var center = map.TranslatePoint(new Point(100, 100), window)!.Value;
            window.MouseWheel(center, new Vector(0, -1), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.True(map.Zoom < zoom);
            zoom = map.Zoom;
            window.MouseWheel(center, new Vector(0, -1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(zoom, map.Zoom);
            Assert.True(map.Offset.Y > 0);
            Assert.True(workspace.Seats[0].IsSelected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LabelContextMenuUsesTheSeatCommands_AndFilteringDisablesIt()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var edits = 0;
        var seat = new SeatItemViewModel("a", "1", true, _ => { edits++; return Task.CompletedTask; }) { LabelText = "靠窗" };
        workspace.Seats[0] = seat;
        workspace.ApplyLayout(workspace.SourceLayout!);
        var view = new SeatWorkspaceView { DataContext = workspace };
        var window = new Window { Content = view, Width = 850, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tile = view.GetLogicalDescendants().OfType<SeatTile>().First();
            var toggle = tile.FindControl<ToggleButton>("SeatToggle")!;
            var point = toggle.TranslatePoint(new Point(26, 26), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            var menu = toggle.ContextMenu!;
            Assert.True(menu.IsOpen);
            var item = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal("编辑标签", item.Header);
            Assert.Same(seat.EditLabelCommand, item.Command);
            await seat.EditLabelCommand.ExecuteAsync(null);
            Assert.Equal(1, edits);
            Assert.False(seat.IsSelected);
            workspace.SeatFilterText = "不匹配";
            await workspace.RefreshAsync();
            Assert.False(toggle.IsEnabled);
            Assert.False(menu.IsEnabled);
            menu.Close();
        }
        finally { window.Close(); }
    }
}
