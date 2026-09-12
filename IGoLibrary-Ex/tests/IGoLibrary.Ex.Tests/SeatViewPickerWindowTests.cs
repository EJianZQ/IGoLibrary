using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatViewPickerWindowTests
{
    [AvaloniaTheory]
    [InlineData(1000, 680)]
    [InlineData(1188, 840)]
    public async Task PickerRestoresSavedChoice_AndAvailabilityCheckboxOnlyAppearsInList(int width, int height)
    {
        var settings = new FakeSettingsService(AppSettings.Default with
        {
            Ui = AppSettings.Default.Ui with { SeatWorkspaceListView = true }
        });
        var vm = MainWindowViewModelTests.CreateViewModel(settingsService: settings);
        await vm.InitializeAsync();
        var workspace = vm.MultiSeatSelection.Workspace;
        SeatWorkspaceMapTests.Populate(workspace, SeatLayoutTestData.Small());
        vm.IsGrabSeatSelectionOverlayOpen = true;
        var window = new MainWindow { DataContext = vm, Width = width, Height = height };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var picker = window.GetLogicalDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, workspace.ViewModes));
            var row = Assert.IsType<Grid>(picker.Parent);
            var input = row.Children.OfType<TextBox>().Single();
            var available = row.Children.OfType<CheckBox>().Single();
            Assert.Equal(1, picker.SelectedIndex);
            Assert.True(available.IsVisible);
            Assert.True(input.Bounds.Width > 30);
            Assert.True(row.Children.All(child => child.Bounds.Right <= row.Bounds.Width + 1));
            picker.SelectedIndex = 0;
            await workspace.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(workspace.IsMapMode);
            Assert.False(available.IsVisible);
            await vm.FlushPersistentDataAsync();
            Assert.False(settings.CurrentSettings.Ui.SeatWorkspaceListView);
            Assert.DoesNotContain(window.GetLogicalDescendants().OfType<CheckBox>(), c => Equals(c.Content, "列表"));
        }
        finally
        {
            vm.IsGrabSeatSelectionOverlayOpen = false;
            window.DataContext = null;
            window.Close();
        }
    }
}
