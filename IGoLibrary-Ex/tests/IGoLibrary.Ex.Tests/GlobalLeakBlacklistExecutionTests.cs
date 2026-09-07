using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakBlacklistExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execution_SkipsBlacklistedSeats_WithoutChangingPriorityOrRequestCounts(bool allFirstVenueBlocked)
    {
        using var fixture = new Fixture();
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>>
        {
            [1] = allFirstVenueBlocked ? [new("a", "旧名称"), new("b", "002")] : [new("a", "旧名称")]
        });
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(Layout(id,
            new("a", "改名", false, 0, 0), new("b", "改名", false, 1, 0)));
        var reserved = new List<(int, string)>();
        fixture.Api.OnReserveSeatAsync = (_, id, key, _) => { reserved.Add((id, key)); return Task.FromResult(true); };
        await fixture.Coordinator.StartAsync(Plan());
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(allFirstVenueBlocked ? [(2, "a")] : [(1, "b")], reserved);
        Assert.Equal(allFirstVenueBlocked ? 3 : 2, fixture.Coordinator.GetStatus().RequestCount);
        Assert.Equal(CoordinatorStatusReason.GlobalLeakSucceeded, fixture.Coordinator.GetStatus().Reason);
        Assert.Contains(fixture.Log.Entries, entry => entry.Message.Contains("黑名单排除"));
    }

    [Fact]
    public async Task AllBlocked_RechecksEveryRound_UsesFrozenSnapshot_AndCancelsWait()
    {
        using var fixture = new Fixture();
        fixture.Runtime.BlockDelaysStartingAtCall = 2;
        var secondDelayStarted = Signal();
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] });
        var calls = 0;
        fixture.Api.OnGetLibraryLayoutAsync = async (_, id, _) =>
        {
            calls++;
            if (calls == 2) fixture.Runtime.DelayStarted = secondDelayStarted;
            // Simulates a settings snapshot being replaced externally after task startup.
            await fixture.Settings.SaveAsync(AppSettings.Default);
            return Layout(id, new SeatSnapshot("a", "1", false, 0, 0));
        };
        fixture.Api.OnReserveSeatAsync = (_, _, _, _) => throw new Xunit.Sdk.XunitException("黑名单座位不应发送预约请求");
        await fixture.Coordinator.StartAsync(new GlobalLeakPlan([new(1, "场馆", "1层")], TimeSpan.FromSeconds(3)));
        await secondDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal(2, fixture.Coordinator.GetStatus().RequestCount);
        await fixture.Coordinator.StopAsync();
        Assert.Equal(CoordinatorStatusReason.Stopped, fixture.Coordinator.GetStatus().Reason);
    }

    [Fact]
    public async Task SettingsReadFailure_FailsBeforeAnyNetworkRequest()
    {
        using var fixture = new Fixture();
        var errorWritten = Signal();
        fixture.Log.EntryWritten += (_, entry) =>
        {
            if (entry.Kind == LogEntryKind.Error) errorWritten.TrySetResult(null);
        };
        fixture.Settings.LoadExceptions.Enqueue(new InvalidOperationException("黑名单损坏"));
        fixture.Api.OnGetLibraryLayoutAsync = (_, _, _) => throw new Xunit.Sdk.XunitException("不应发送网络请求");
        await fixture.Coordinator.StartAsync(Plan());
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await errorWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CoordinatorTaskState.Failed, fixture.Coordinator.GetStatus().State);
        Assert.Equal(0, fixture.Coordinator.GetStatus().RequestCount);
        Assert.Contains(fixture.Log.Entries, entry => entry.Kind == LogEntryKind.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveBeforeStart_IsSerialized_AndFailedSaveKeepsPreviousConfiguration(bool failSave)
    {
        using var fixture = new Fixture();
        var release = Signal();
        fixture.Settings.UpdateStarted = Signal();
        fixture.Settings.UpdateBlocker = release.Task;
        if (failSave) fixture.Settings.UpdateExceptions.Enqueue(new IOException("保存失败"));
        var save = fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] });
        await fixture.Settings.UpdateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        string? reserved = null;
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(Layout(id,
            new("a", "1", false, 0, 0), new("b", "2", false, 1, 0)));
        fixture.Api.OnReserveSeatAsync = (_, _, key, _) => { reserved = key; return Task.FromResult(true); };
        var start = fixture.Coordinator.StartAsync(Plan());
        Assert.False(start.IsCompleted);
        release.SetResult(null);
        if (failSave) await Assert.ThrowsAsync<IOException>(() => save);
        else await save;
        await start;
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(failSave ? "a" : "b", reserved);
    }

    [Fact]
    public async Task StartBeforeSave_RejectsMutation_WhileNetworkRequestIsPending()
    {
        using var fixture = new Fixture();
        var requested = Signal();
        var release = Signal();
        fixture.Api.OnGetLibraryLayoutAsync = async (_, id, token) =>
        {
            requested.SetResult(null);
            await release.Task.WaitAsync(token);
            return Layout(id);
        };
        await fixture.Coordinator.StartAsync(Plan());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Blacklist.SaveAsync(
            new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] }));
        Assert.Equal(0, fixture.Settings.SaveCalls);
        await fixture.Coordinator.StopAsync();
    }

    [Theory]
    [InlineData("a", "A,b")]
    [InlineData("A", "a,b")]
    [InlineData("missing", "a,A,b")]
    public void Filter_IsOrdinal_PreservesLayoutOrder_AndExcludesOccupied(string excluded, string expected)
    {
        var result = GlobalLeakStateMachine.GetAvailableSeats(Layout(1,
            new("a", "001", false, 0, 0), new("A", "001", false, 1, 0),
            new("occupied", "002", true, 2, 0), new("b", "003", false, 3, 0)),
            new HashSet<string>([excluded], StringComparer.Ordinal));
        Assert.Equal(expected.Split(','), result.Select(static seat => seat.SeatKey));
    }

    internal static GlobalLeakPlan Plan() => new([new(1, "场馆A", "1层"), new(2, "场馆B", "2层")], TimeSpan.FromSeconds(2));
    internal static LibraryLayout Layout(int id, params SeatSnapshot[] seats) => new(id, $"场馆{id}", "1层", true, seats.Length, 0, 0, seats);
    internal static TaskCompletionSource<object?> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Fixture : IDisposable
    {
        public FakeTraceIntApiClient Api { get; } = new();
        public FakeCoordinatorRuntime Runtime { get; } = new();
        public FakeSettingsService Settings { get; } = new(AppSettings.Default);
        public ActivityLogService Log { get; } = new();
        public GlobalLeakConfigurationGate Gate { get; } = new();
        public GlobalLeakCoordinator Coordinator { get; }
        public GlobalLeakSeatBlacklistService Blacklist { get; }
        public TaskCompletionSource<object?> Terminal { get; } = Signal();
        public Fixture(Microsoft.Extensions.Logging.ILogger<GlobalLeakWorkflowRunner>? runnerLogger = null,
            Microsoft.Extensions.Logging.ILogger<GlobalLeakSeatBlacklistService>? blacklistLogger = null)
        {
            var runner = new GlobalLeakWorkflowRunner(Api, new FakeCoordinatorEventPublisher(), Log,
                new AppRuntimeState { Session = new("cookie", SessionSource.ManualCookie, DateTimeOffset.Now, true) },
                Runtime, Settings, runnerLogger ?? NullLogger<GlobalLeakWorkflowRunner>.Instance);
            Coordinator = new GlobalLeakCoordinator(runner, Runtime, Gate);
            Coordinator.StatusChanged += (_, status) =>
            {
                if (status.State is CoordinatorTaskState.Completed or CoordinatorTaskState.Failed) Terminal.TrySetResult(null);
            };
            Blacklist = new GlobalLeakSeatBlacklistService(Settings, Coordinator, Gate, Log, blacklistLogger ?? NullLogger<GlobalLeakSeatBlacklistService>.Instance);
        }
        public void Dispose() => Gate.Dispose();
    }
}
