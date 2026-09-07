using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class GlobalLeakBlacklistEditorTests
{
    [AvaloniaFact]
    public async Task Labels_AreSearchable_AndLabelFailureStillAllowsSeatSelection()
    {
        using var fixture = new Fixture();
        fixture.Labels.LabelsByLibraryId[1] = [new("a", "001", "靠窗")];
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        Assert.Equal("靠窗", fixture.Editor.Workspace.Seats[0].LabelText);
        fixture.Editor.Workspace.SeatFilterText = "靠窗";
        await fixture.Editor.Workspace.RefreshAsync();
        Assert.True(fixture.Editor.Workspace.Seats[0].IsFilterVisible);
        Assert.False(fixture.Editor.Workspace.Seats[1].IsFilterVisible);
        fixture.Labels.GetException = new IOException("标签读取失败");
        fixture.Editor.Workspace.SeatFilterText = string.Empty;
        await fixture.Editor.RefreshCommand.ExecuteAsync(null);
        Assert.True(fixture.Editor.CanEditSeats);
        Assert.All(fixture.Editor.Workspace.Seats, seat => Assert.False(seat.SupportsLabelEditing));
        fixture.Editor.Workspace.Seats[0].IsSelected = true;
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
    }

    [AvaloniaFact]
    public async Task MultipleVenues_SaveTogether_AndFilterDoesNotLoseOccupiedSelections()
    {
        using var fixture = new Fixture();
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        var first = fixture.Editor.Workspace.Seats[0];
        var occupied = fixture.Editor.Workspace.Seats[1];
        first.IsSelected = true;
        occupied.IsSelected = true;
        fixture.Editor.Workspace.ShowAvailableOnly = true;
        await fixture.Editor.Workspace.RefreshAsync();
        Assert.False(occupied.IsFilterVisible);
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[1];
        await fixture.Editor.PendingLoad;
        fixture.Editor.Workspace.Seats[0].IsSelected = true;
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[0];
        await fixture.Editor.PendingLoad;
        Assert.All(fixture.Editor.Workspace.Seats, seat => Assert.True(seat.IsSelected));
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.False(fixture.Editor.IsOpen);
        Assert.Equal(3, fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats.Count);
        Assert.Equal(1, fixture.Settings.SaveCalls);
        Assert.Equal(0, fixture.Labels.SetCalls);
        Assert.Null(fixture.Library.BoundLibrary);
    }

    [AvaloniaFact]
    public async Task CancelAndReopen_DiscardsDraft_AndRetainsUnselectedVenueRecords()
    {
        using var fixture = new Fixture();
        await fixture.SaveInitialAsync();
        await fixture.Editor.OpenAsync([GlobalLeakBlacklistExecutionTests.Plan().Libraries[0]]);
        fixture.Editor.ClearCurrentVenueCommand.Execute(null);
        fixture.Editor.CancelCommand.Execute(null);
        await fixture.Editor.OpenAsync([GlobalLeakBlacklistExecutionTests.Plan().Libraries[0]]);
        Assert.True(fixture.Editor.Workspace.Seats[0].IsSelected);
        fixture.Editor.ClearCurrentVenueCommand.Execute(null);
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(2, Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats).LibraryId);
    }

    [AvaloniaFact]
    public async Task MissingSeats_AreRetainedAcrossRefresh_AndExplicitlyRemovable()
    {
        using var fixture = new Fixture();
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("missing", "旧座位")] });
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        Assert.Single(fixture.Editor.MissingSeats);
        await fixture.Editor.RefreshCommand.ExecuteAsync(null);
        fixture.Editor.RemoveMissingSeatCommand.Execute(Assert.Single(fixture.Editor.MissingSeats));
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Empty(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
    }

    [AvaloniaFact]
    public async Task SettingsFailure_IsNotEmptyBlacklist_AndRetryRestoresSavedSeats()
    {
        using var fixture = new Fixture();
        await fixture.SaveInitialAsync();
        fixture.Settings.LoadExceptions.Enqueue(new IOException("读取失败"));
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        Assert.True(fixture.Editor.HasError);
        Assert.False(fixture.Editor.CanSave);
        await fixture.Editor.RefreshCommand.ExecuteAsync(null);
        Assert.True(fixture.Editor.CanSave);
        Assert.True(fixture.Editor.Workspace.Seats[0].IsSelected);
    }

    [AvaloniaFact]
    public async Task LayoutFailure_DoesNotDeleteThatVenue_WhenAnotherVenueIsSaved()
    {
        using var fixture = new Fixture();
        await fixture.SaveInitialAsync();
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => id == 1
            ? Task.FromException<LibraryLayout>(new IOException("布局失败")) : Task.FromResult(Fixture.Layout(id));
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        Assert.True(fixture.Editor.HasError);
        Assert.False(fixture.Editor.CanEditSeats);
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[1];
        await fixture.Editor.PendingLoad;
        fixture.Editor.ClearCurrentVenueCommand.Execute(null);
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats).LibraryId);
    }

    [AvaloniaFact]
    public async Task SaveFailure_KeepsDraft_AndSavingLocksAllMutation()
    {
        using var fixture = new Fixture();
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        fixture.Editor.Workspace.Seats[0].IsSelected = true;
        var release = GlobalLeakBlacklistExecutionTests.Signal();
        fixture.Settings.UpdateBlocker = release.Task;
        fixture.Settings.UpdateStarted = GlobalLeakBlacklistExecutionTests.Signal();
        fixture.Settings.UpdateExceptions.Enqueue(new IOException("写入失败"));
        var saving = fixture.Editor.SaveCommand.ExecuteAsync(null);
        await fixture.Settings.UpdateStarted.Task;
        Assert.False(fixture.Editor.CanClose);
        Assert.False(fixture.Editor.CanSave);
        Assert.False(fixture.Editor.CanSwitchVenue);
        var originalVenue = fixture.Editor.SelectedVenue;
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[1];
        Assert.Same(originalVenue, fixture.Editor.SelectedVenue);
        fixture.Editor.CancelCommand.Execute(null);
        fixture.Editor.ClearCurrentVenueCommand.Execute(null);
        fixture.Editor.Workspace.Seats[0].IsSelected = false;
        Assert.True(fixture.Editor.Workspace.Seats[0].IsSelected);
        release.SetResult(null);
        await saving;
        Assert.True(fixture.Editor.IsOpen);
        Assert.True(fixture.Editor.CanSave);
        Assert.Empty(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
    }

    [AvaloniaFact]
    public async Task RapidVenueSwitch_IgnoresOldResponses_WithoutUnlockingLatestLoad()
    {
        using var fixture = new Fixture();
        var oldA = new TaskCompletionSource<LibraryLayout>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newA = new TaskCompletionSource<LibraryLayout>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = GlobalLeakBlacklistExecutionTests.Signal();
        var calls = 0;
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) =>
        {
            if (id == 2) return Task.FromResult(Fixture.Layout(id));
            started.TrySetResult(null);
            return ++calls == 1 ? oldA.Task : newA.Task;
        };
        var opening = fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        await started.Task;
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[1];
        await fixture.Editor.PendingLoad;
        fixture.Editor.SelectedVenue = fixture.Editor.Venues[0];
        var latest = fixture.Editor.PendingLoad;
        oldA.SetResult(GlobalLeakBlacklistExecutionTests.Layout(1, new SeatSnapshot("old", "旧响应", false, 0, 0)));
        await opening;
        Assert.True(fixture.Editor.IsLoading);
        Assert.Empty(fixture.Editor.Workspace.Seats);
        newA.SetResult(Fixture.Layout(1));
        await latest;
        Assert.Equal("a", fixture.Editor.Workspace.Seats[0].SeatKey);
        Assert.True(fixture.Editor.CanEditSeats);
    }

    [AvaloniaFact]
    public async Task CloseDuringRefresh_AndReopen_IgnoresLateResponse()
    {
        using var fixture = new Fixture();
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        var response = new TaskCompletionSource<LibraryLayout>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.OnGetLibraryLayoutAsync = (_, _, _) => response.Task;
        var refresh = fixture.Editor.RefreshCommand.ExecuteAsync(null);
        fixture.Editor.ResetSession();
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(Fixture.Layout(id));
        await fixture.Editor.OpenAsync([GlobalLeakBlacklistExecutionTests.Plan().Libraries[1]]);
        response.SetResult(GlobalLeakBlacklistExecutionTests.Layout(1, new SeatSnapshot("old", "旧响应", false, 0, 0)));
        await refresh;
        Assert.Equal(2, fixture.Editor.SelectedVenue!.Target.LibraryId);
        Assert.Equal("a", fixture.Editor.Workspace.Seats[0].SeatKey);
    }

    [AvaloniaTheory]
    [InlineData(CoordinatorTaskState.Starting)]
    [InlineData(CoordinatorTaskState.Running)]
    [InlineData(CoordinatorTaskState.Stopping)]
    public async Task RunningEditor_IsReadOnly_AndCanCancel(CoordinatorTaskState state)
    {
        using var fixture = new Fixture();
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        fixture.Coordinator.EmitStatus(CoordinatorStatus.Idle("测试") with { State = state });
        fixture.Editor.IsTaskActive = true;
        fixture.Editor.Workspace.Seats[0].IsSelected = true;
        Assert.False(fixture.Editor.Workspace.Seats[0].IsSelected);
        await fixture.Editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, fixture.Settings.SaveCalls);
        fixture.Editor.CancelCommand.Execute(null);
        Assert.False(fixture.Editor.IsOpen);
    }

    internal sealed class Fixture : IDisposable
    {
        public FakeSettingsService Settings { get; } = new(AppSettings.Default);
        public FakeTraceIntApiClient Api { get; } = new();
        public FakeLibraryService Library { get; } = new();
        public FakeSeatLabelService Labels { get; } = new();
        public FakeGlobalLeakCoordinator Coordinator { get; } = new();
        public GlobalLeakConfigurationGate Gate { get; } = new();
        public GlobalLeakSeatBlacklistService Blacklist { get; }
        public GlobalLeakSeatBlacklistEditorViewModel Editor { get; }
        public Fixture()
        {
            var activity = new ActivityLogService();
            Blacklist = new GlobalLeakSeatBlacklistService(Settings, Coordinator, Gate, activity, NullLogger<GlobalLeakSeatBlacklistService>.Instance);
            Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(Layout(id));
            var session = new FakeSessionService { CurrentSession = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true) };
            var venues = new VenueWorkflowService(Library, Labels, session, Api, Settings);
            Editor = new GlobalLeakSeatBlacklistEditorViewModel(Blacklist, venues, Labels, activity,
                new FakeNotificationService(), NullLogger<GlobalLeakSeatBlacklistEditorViewModel>.Instance);
        }
        public Task SaveInitialAsync() => Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>>
        { [1] = [new("a", "001")], [2] = [new("a", "001")] });
        public static LibraryLayout Layout(int id) => GlobalLeakBlacklistExecutionTests.Layout(id,
            new("a", "001", false, 0, 0), new("b", "002", true, 1, 0));
        public void Dispose() { Editor.Dispose(); Gate.Dispose(); }
    }
}
