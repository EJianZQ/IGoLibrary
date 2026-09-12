using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatViewPreferenceTests
{
    [AvaloniaFact]
    public async Task ManualChoiceIsSharedAndRestored_AutomaticFallbackDoesNotSave()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var log = new ActivityLogService();
        var preferences = new SeatViewPreferenceService(new SettingsWorkflowService(settings), log);
        using var first = new SeatWorkspaceViewModel(log, viewPreferences: preferences);
        using var second = new SeatWorkspaceViewModel(log, viewPreferences: preferences);
        await first.InitializeViewPreferenceAsync();
        SeatWorkspaceMapTests.Populate(first, SeatLayoutTestData.Small());
        SeatWorkspaceMapTests.Populate(second, SeatLayoutTestData.Small(2));
        Assert.Equal(0, first.SelectedViewIndex);
        var picker = new ComboBox { DataContext = first, ItemsSource = first.ViewModes };
        picker.Bind(ComboBox.SelectedIndexProperty, new Binding(nameof(first.SelectedViewIndex)) { Mode = BindingMode.TwoWay });
        picker.SelectedIndex = 1;
        await first.FlushViewPreferenceAsync();
        Assert.True(settings.CurrentSettings.Ui.SeatWorkspaceListView);
        Assert.True(second.IsListMode);
        var restored = new SeatViewPreferenceService(new SettingsWorkflowService(settings), log);
        using var restarted = new SeatWorkspaceViewModel(log, viewPreferences: restored);
        await restarted.InitializeViewPreferenceAsync();
        SeatWorkspaceMapTests.Populate(restarted, SeatLayoutTestData.Small());
        Assert.True(restarted.IsListMode);
        picker.SelectedIndex = 0;
        await first.FlushViewPreferenceAsync();
        var saves = settings.SaveCalls;
        var invalid = SeatLayoutTestData.Small() with { Seats = [new("a", "1", false, 0, 0), new("b", "2", false, 0, 0)] };
        SeatWorkspaceMapTests.Populate(first, invalid);
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsListMode);
        Assert.Equal(1, picker.SelectedIndex);
        await first.FlushViewPreferenceAsync();
        Assert.Equal(saves, settings.SaveCalls);
        Assert.False(settings.CurrentSettings.Ui.SeatWorkspaceListView);
        SeatWorkspaceMapTests.Populate(first, SeatLayoutTestData.Small());
        Assert.True(first.IsMapMode);
    }

    [AvaloniaFact]
    public async Task AvailabilityFilterOnlyAppliesToList_AndKeepsSelectionAndTextFilter()
    {
        using var workspace = SeatWorkspaceMapTests.CreateWorkspace();
        var occupied = workspace.Seats[1];
        occupied.IsSelected = true;
        workspace.ShowAvailableOnly = true;
        await workspace.RefreshAsync();
        Assert.True(occupied.IsFilterVisible);
        workspace.SelectedViewIndex = 1;
        await workspace.RefreshAsync();
        Assert.False(occupied.IsFilterVisible);
        workspace.SelectedViewIndex = 0;
        await workspace.RefreshAsync();
        Assert.True(occupied.IsFilterVisible);
        Assert.True(occupied.IsSelected);
        Assert.True(workspace.ShowAvailableOnly);
        workspace.SeatFilterText = "不匹配";
        await workspace.RefreshAsync();
        Assert.False(occupied.IsFilterVisible);
    }

    [AvaloniaFact]
    public async Task QueuedChangesSaveInOrder_AndDoNotOverwriteOtherSettings()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var blocker = new TaskCompletionSource();
        settings.UpdateBlocker = blocker.Task;
        var workflow = new SettingsWorkflowService(settings);
        var preferences = new SeatViewPreferenceService(workflow, new ActivityLogService());
        await preferences.InitializeAsync();
        preferences.Select(true);
        preferences.Select(false);
        preferences.Select(true);
        var sizeSave = workflow.SaveMainViewSizeAsync(1300, 800);
        blocker.SetResult();
        await preferences.FlushAsync();
        await sizeSave;
        Assert.True(settings.CurrentSettings.Ui.SeatWorkspaceListView);
        Assert.Equal(1300, settings.CurrentSettings.Ui.MainViewSize!.ClientWidth);
    }

    [AvaloniaFact]
    public async Task SaveFailureIsReported_AndNextChoiceCanSave()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        settings.UpdateExceptions.Enqueue(new IOException("保存失败"));
        var preferences = new SeatViewPreferenceService(new SettingsWorkflowService(settings), new ActivityLogService());
        preferences.Select(true);
        await Assert.ThrowsAsync<IOException>(preferences.FlushAsync);
        Assert.True(preferences.PreferList);
        Assert.False(settings.CurrentSettings.Ui.SeatWorkspaceListView);
        preferences.Select(false);
        await preferences.FlushAsync();
    }
}
