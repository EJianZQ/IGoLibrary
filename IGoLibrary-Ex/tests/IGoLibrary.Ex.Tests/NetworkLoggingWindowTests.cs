using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class NetworkLoggingWindowTests
{
    [AvaloniaFact]
    public async Task NetworkToggleIsInLoggingCardAndTwoWayBindsEvenWhenMasterIsOff()
    {
        var viewModel = MainWindowViewModelTests.CreateViewModel();
        var storage = viewModel.SystemSettings.StorageSettings;
        await storage.InitializeAsync(new LogFileSettings(false, 30));
        var window = new MainWindow { DataContext = viewModel };
        var toggle = Assert.IsType<ToggleSwitch>(window.FindControl<ToggleSwitch>("RecordNetworkRequestsToggle"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(toggle.IsChecked);
        Assert.True(toggle.IsEnabled);
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        await storage.FlushPendingLoggingSettingsSaveAsync();
        Assert.True(storage.RecordNetworkRequests);
        var grid = Assert.IsType<Grid>(toggle.Parent);
        Assert.Equal(1, Grid.GetRow(toggle));
        Assert.Contains(grid.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "记录网络请求");
        window.Close();
    }
}
