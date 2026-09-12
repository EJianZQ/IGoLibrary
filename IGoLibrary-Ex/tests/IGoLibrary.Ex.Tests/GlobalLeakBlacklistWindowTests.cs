using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Controls;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class GlobalLeakBlacklistWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Modal_BlocksSidebarClicks_AndRestoresNavigationAfterClosing(bool save)
    {
        var api = new FakeTraceIntApiClient { OnGetLibraryLayoutAsync = (_, id, _) =>
            Task.FromResult(GlobalLeakBlacklistEditorTests.Fixture.Layout(id)) };
        var viewModel = MainWindowViewModelTests.CreateGlobalLeakViewModel(apiClient: api);
        await viewModel.InitializeAsync();
        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);
        viewModel.SelectedTabIndex = 3;
        var window = new MainWindow { DataContext = viewModel, Width = 1188, Height = 840 };
        try
        {
            window.Show();
            var sidebar = window.FindControl<ListBox>("SidebarNavigationList")!;
            Assert.True(sidebar.IsHitTestVisible);

            await viewModel.ManageGlobalLeakBlacklistCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var editor = viewModel.GlobalLeakPage.BlacklistEditor;
            Assert.True(viewModel.HasOpenModalOverlay);
            Assert.False(sidebar.IsHitTestVisible);
            var home = Assert.IsAssignableFrom<Control>(sidebar.ContainerFromIndex(0));
            var point = home.TranslatePoint(new Point(home.Bounds.Width / 2, home.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsOpen);
            Assert.Equal(3, viewModel.SelectedTabIndex);

            if (save)
            {
                editor.Workspace.Seats[0].IsSelected = true;
                await editor.SaveCommand.ExecuteAsync(null);
            }
            else
            {
                editor.CancelCommand.Execute(null);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsOpen);
            Assert.False(viewModel.HasOpenModalOverlay);
            Assert.True(sidebar.IsHitTestVisible);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, viewModel.SelectedTabIndex);
        }
        finally
        {
            viewModel.GlobalLeakPage.BlacklistEditor.ResetSession();
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeLayout_ScrollsInsideModal_AndUsesThemeResources(bool dark)
    {
        var api = new FakeTraceIntApiClient { OnGetLibraryLayoutAsync = (_, id, _) =>
            Task.FromResult(GlobalLeakBlacklistExecutionTests.Layout(id,
                Enumerable.Range(1, 180).Select(index => new SeatSnapshot($"seat-{index}", index.ToString("D3"), index % 3 == 0, index, 0)).ToArray())) };
        var viewModel = MainWindowViewModelTests.CreateGlobalLeakViewModel(apiClient: api);
        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        viewModel.GlobalLeakLibraries[1].IsSelected = true;
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);
        var window = new MainWindow { DataContext = viewModel, Width = 1000, Height = 680,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show();
            await viewModel.ManageGlobalLeakBlacklistCommand.ExecuteAsync(null);
            var editor = viewModel.GlobalLeakPage.BlacklistEditor;
            editor.Workspace.Seats[2].IsSelected = true;
            editor.Workspace.Seats[4].IsSelected = true;
            editor.Workspace.Seats[4].LabelText = "靠窗";
            Dispatcher.UIThread.RunJobs();
            var modal = window.FindControl<Border>("GlobalLeakBlacklistModal")!;
            var workspace = modal.GetLogicalDescendants().OfType<SeatWorkspaceView>().Single();
            editor.Workspace.IsListMode = true;
            Dispatcher.UIThread.RunJobs();
            var scroll = workspace.GetLogicalDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var save = modal.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "确认保存"));
            var bottom = save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y;
            Assert.True(bottom <= window.ClientSize.Height);

        }
        finally
        {
            viewModel.GlobalLeakPage.BlacklistEditor.ResetSession();
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EmptySelection_ButtonRemainsClickable_AndOnlyShowsToast()
    {
        var notifications = new FakeNotificationService();
        var viewModel = MainWindowViewModelTests.CreateGlobalLeakViewModel(notificationService: notifications);
        var window = new MainWindow { DataContext = viewModel };
        var button = window.FindControl<Button>("ManageGlobalLeakBlacklistButton")!;
        Assert.True(button.IsEnabled);
        Assert.Equal("管理黑名单座位", button.Content);
        Assert.Same(viewModel.ManageGlobalLeakBlacklistCommand, button.Command);
        await viewModel.ManageGlobalLeakBlacklistCommand.ExecuteAsync(null);
        Assert.Contains(notifications.Warnings, warning => warning.Message == "请先选择至少一个场馆");
        Assert.False(viewModel.GlobalLeakPage.BlacklistEditor.IsOpen);
        Assert.Equal(0, Grid.GetColumn(button));
        var interval = button.Parent!.GetLogicalDescendants().OfType<NumericUpDown>().Single();
        Assert.Equal(1, Grid.GetColumn(interval.GetLogicalAncestors().OfType<StackPanel>().First()));
    }

    [AvaloniaTheory]
    [InlineData(1000, 680)]
    [InlineData(1188, 840)]
    public async Task Modal_UsesSharedCardsAndFitsWindow_AndMobileStartDisablesEditing(int width, int height)
    {
        var api = new FakeTraceIntApiClient { OnGetLibraryLayoutAsync = (_, id, _) =>
            Task.FromResult(GlobalLeakBlacklistEditorTests.Fixture.Layout(id)) };
        var coordinator = new FakeGlobalLeakCoordinator();
        var settings = new FakeSettingsService(IGoLibrary.Ex.Application.Configuration.AppSettings.Default);
        var viewModel = MainWindowViewModelTests.CreateGlobalLeakViewModel(settingsService: settings, apiClient: api, globalLeakCoordinator: coordinator);
        await viewModel.OpenGlobalLeakLibraryPickerCommand.ExecuteAsync(null);
        viewModel.GlobalLeakLibraries[0].IsSelected = true;
        await viewModel.ConfirmGlobalLeakLibrariesCommand.ExecuteAsync(null);
        var window = new MainWindow { DataContext = viewModel, Width = width, Height = height };
        try
        {
            window.Show();
            await viewModel.ManageGlobalLeakBlacklistCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var modal = window.FindControl<Border>("GlobalLeakBlacklistModal")!;
            Assert.True(modal.IsVisible);
            Assert.True(modal.Bounds.Height > 0);
            Assert.True(modal.Bounds.Height <= height);
            var view = modal.GetLogicalDescendants().OfType<GlobalLeakSeatBlacklistView>().Single();
            var workspace = view.GetLogicalDescendants().OfType<SeatWorkspaceView>().Single();
            Assert.Same(viewModel.GlobalLeakPage.BlacklistEditor.Workspace, workspace.DataContext);
            Assert.True(workspace.IsEnabled);
            var toggles = workspace.GetLogicalDescendants().OfType<SeatTile>()
                .Select(tile => tile.FindControl<Avalonia.Controls.Primitives.ToggleButton>("SeatToggle")!).ToArray();
            Assert.Equal(2, toggles.Length);
            toggles[0].IsChecked = true;
            Assert.True(viewModel.GlobalLeakPage.BlacklistEditor.Workspace.Seats[0].IsSelected);
            coordinator.EmitStatus(CoordinatorStatus.Idle("全域捡漏") with { State = CoordinatorTaskState.Running });
            Dispatcher.UIThread.RunJobs();
            Assert.False(workspace.IsEnabled);
            var save = view.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "确认保存"));
            Assert.False(save.IsEnabled);
            Assert.True(viewModel.GlobalLeakPage.BlacklistEditor.CanClose);
            coordinator.EmitStatus(CoordinatorStatus.Idle("全域捡漏"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            var clickPoint = save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), window)!.Value;
            window.MouseDown(clickPoint, MouseButton.Left);
            window.MouseUp(clickPoint, MouseButton.Left);
            await viewModel.GlobalLeakPage.BlacklistEditor.SaveCommand.ExecutionTask!;
            Assert.False(viewModel.GlobalLeakPage.BlacklistEditor.IsOpen);
            var saved = Assert.Single(settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
            Assert.Equal(1, saved.LibraryId);
            Assert.Equal("a", saved.SeatKey);
            Assert.Equal(1, Assert.Single(settings.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries).LibraryId);
        }
        finally
        {
            viewModel.GlobalLeakPage.BlacklistEditor.ResetSession();
            window.DataContext = null;
            window.Close();
        }
    }
}
