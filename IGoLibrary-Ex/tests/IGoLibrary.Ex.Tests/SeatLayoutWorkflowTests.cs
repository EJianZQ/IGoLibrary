using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class SeatLayoutWorkflowTests
{
    [AvaloniaFact]
    public async Task RefreshDuringBinding_DoesNotCancelCommittedVenue_AndResumesAfterCompletion()
    {
        var first = new LibrarySummary(1, "A", "2F", true);
        var second = new LibrarySummary(2, "B", "3F", true);
        var service = new FakeLibraryService { LibrariesToLoad = [first, second] };
        service.LayoutsByLibraryId[1] = SeatLayoutTestData.Small(1);
        service.LayoutsByLibraryId[2] = SeatLayoutTestData.Small(2);
        var api = new FakeTraceIntApiClient();
        var session = new FakeSessionService { CurrentSession = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true) };
        var root = MainWindowViewModelTests.CreateViewModel(libraryService: service, sessionService: session, apiClient: api);
        root.IsAuthorized = true;
        root.SelectedLibrary = first;
        await root.BindSelectedLibraryCommand.ExecuteAsync(null);

        var rule = new TaskCompletionSource<LibraryRule>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken bindingToken = default;
        api.OnGetLibraryRuleAsync = (_, _, token) => { bindingToken = token; return rule.Task.WaitAsync(token); };
        var refreshAvailability = new List<bool>();
        root.RefreshSeatsCommand.CanExecuteChanged += (_, _) => refreshAvailability.Add(root.RefreshSeatsCommand.CanExecute(null));
        root.SelectedLibrary = second;
        var bind = root.BindSelectedLibraryCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(2, service.BoundLibrary!.LibraryId);
            Assert.Equal(1, root.AccountVenue.LockedLibrary!.LibraryId);
            Assert.False(root.RefreshSeatsCommand.CanExecute(null));
            // ExecuteAsync can bypass CanExecute, so the command body must protect binding too.
            await root.RefreshSeatsCommand.ExecuteAsync(null);
            await root.BindSelectedLibraryCommand.ExecuteAsync(null);
            Assert.False(bindingToken.IsCancellationRequested);
            Assert.False(bind.IsCompleted);
            Assert.Equal(0, service.RefreshBoundLibraryCalls);
            Assert.Equal(2, service.BindLibraryCalls);
        }
        finally
        {
            // A rule failure is non-fatal; the loaded layout must still become the bound venue.
            rule.TrySetException(new IOException("规则暂不可用"));
            await bind;
        }

        Assert.Equal(2, root.AccountVenue.LockedLibrary!.LibraryId);
        Assert.Equal(2, root.MultiSeatSelection.Workspace.LibraryId);
        Assert.True(root.RefreshSeatsCommand.CanExecute(null));
        Assert.Contains(false, refreshAvailability);
        Assert.True(refreshAvailability[^1]);
        await root.RefreshSeatsCommand.ExecuteAsync(null);
        Assert.Equal(1, service.RefreshBoundLibraryCalls);
        Assert.Equal(service.BoundLibrary!.LibraryId, root.AccountVenue.LockedLibrary.LibraryId);
    }

    [AvaloniaFact]
    public async Task ClearedBinding_CompletingLateCannotUnlockRefreshDuringNewBinding()
    {
        var library = new LibrarySummary(1, "A", "2F", true);
        var service = new FakeLibraryService { LibrariesToLoad = [library] };
        service.LayoutsByLibraryId[1] = SeatLayoutTestData.Small(1);
        var api = new FakeTraceIntApiClient();
        var session = new FakeSessionService { CurrentSession = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true) };
        var root = MainWindowViewModelTests.CreateViewModel(libraryService: service, sessionService: session, apiClient: api);
        root.IsAuthorized = true;
        var oldRule = new TaskCompletionSource<LibraryRule>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRule = new TaskCompletionSource<LibraryRule>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        api.OnGetLibraryRuleAsync = (_, _, token) => { oldToken = token; return oldRule.Task; };
        root.SelectedLibrary = library;
        var oldBind = root.BindSelectedLibraryCommand.ExecuteAsync(null);
        root.AccountVenue.ClearVenueState();
        Assert.True(oldToken.IsCancellationRequested);
        Assert.True(root.RefreshSeatsCommand.CanExecute(null));

        api.OnGetLibraryRuleAsync = (_, _, _) => newRule.Task;
        root.SelectedLibrary = library;
        var newBind = root.BindSelectedLibraryCommand.ExecuteAsync(null);
        try
        {
            oldRule.SetException(new IOException("旧规则请求失败"));
            await oldBind;
            Assert.Null(root.AccountVenue.LockedLibrary);
            Assert.False(root.RefreshSeatsCommand.CanExecute(null));
            await root.RefreshSeatsCommand.ExecuteAsync(null);
            Assert.Equal(0, service.RefreshBoundLibraryCalls);
        }
        finally
        {
            newRule.TrySetException(new OperationCanceledException("绑定失败"));
            await newBind;
        }
        Assert.True(root.RefreshSeatsCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task GrabRefreshReturningAfterNewBinding_CannotReplaceLayoutOrLabels()
    {
        var first = new LibrarySummary(1, "A", "2F", true);
        var second = new LibrarySummary(2, "B", "3F", true);
        var service = new FakeLibraryService { LibrariesToLoad = [first, second] };
        service.LayoutsByLibraryId[1] = SeatLayoutTestData.Small(1);
        service.LayoutsByLibraryId[2] = SeatLayoutTestData.Small(2) with { Seats = [new("other", "其他场馆", false, 1, 1)] };
        var session = new FakeSessionService { CurrentSession = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true) };
        var root = MainWindowViewModelTests.CreateViewModel(libraryService: service, sessionService: session);
        root.IsAuthorized = true;
        root.SelectedLibrary = first;
        await root.BindSelectedLibraryCommand.ExecuteAsync(null);
        var result = new TaskCompletionSource<LibraryLayout>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        service.OnRefreshLayoutAsync = token => { oldToken = token; return result.Task; };
        var refresh = root.RefreshSeatsCommand.ExecuteAsync(null);
        root.SelectedLibrary = second;
        await root.BindSelectedLibraryCommand.ExecuteAsync(null);
        Assert.True(oldToken.IsCancellationRequested);
        result.SetResult(service.LayoutsByLibraryId[1]);
        await refresh;
        Assert.Equal(2, root.MultiSeatSelection.Workspace.LibraryId);
        Assert.Equal("other", Assert.Single(root.VisibleSeats).SeatKey);
        Assert.Equal("other", Assert.Single(root.TomorrowVisibleSeats).SeatKey);
    }

    [AvaloniaFact]
    public async Task BlacklistFailedRefresh_PreservesLastMapAndDraft_ForRetry()
    {
        using var fixture = new GlobalLeakBlacklistEditorTests.Fixture();
        await fixture.Editor.OpenAsync(GlobalLeakBlacklistExecutionTests.Plan().Libraries);
        var workspace = fixture.Editor.Workspace;
        workspace.Seats[0].IsSelected = true;
        var projection = workspace.Projection;
        var first = workspace.Seats[0];
        fixture.Api.OnGetLibraryLayoutAsync = (_, _, _) => Task.FromException<LibraryLayout>(new IOException("网络失败"));
        await fixture.Editor.RefreshCommand.ExecuteAsync(null);
        Assert.Same(projection, workspace.Projection);
        Assert.Same(first, workspace.Seats[0]);
        Assert.True(first.IsSelected);
        Assert.True(fixture.Editor.HasError);
        Assert.False(fixture.Editor.CanEditSeats);
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(SeatLayoutTestData.Small(id));
        await fixture.Editor.RefreshCommand.ExecuteAsync(null);
        Assert.True(fixture.Editor.CanEditSeats);
        Assert.True(workspace.Seats[0].IsSelected);
    }
}
