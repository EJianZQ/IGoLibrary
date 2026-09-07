using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakBlacklistServiceTests
{
    [Fact]
    public async Task Save_MergesVenues_Deduplicates_AndDoesNotSaveUnchangedSelection()
    {
        using var fixture = new GlobalLeakBlacklistExecutionTests.Fixture();
        var changes = new Dictionary<int, IReadOnlyList<SeatReference>>
        {
            [1] = [new("a", "1"), new("a", "1"), new("A", "2")],
            [2] = [new("a", "1")]
        };
        await fixture.Blacklist.SaveAsync(changes);
        await fixture.Blacklist.SaveAsync(changes);
        Assert.Equal(1, fixture.Settings.SaveCalls);
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [] });
        var restored = await fixture.Blacklist.LoadAsync([1, 2]);
        Assert.Empty(restored[1]);
        Assert.Single(restored[2]);
        Assert.Equal("a", restored[2][0].SeatKey);
    }

    [Theory]
    [InlineData(CoordinatorTaskState.Starting)]
    [InlineData(CoordinatorTaskState.Running)]
    [InlineData(CoordinatorTaskState.Stopping)]
    public async Task Save_RejectsEveryActiveState(CoordinatorTaskState state)
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var coordinator = new FakeGlobalLeakCoordinator();
        coordinator.EmitStatus(CoordinatorStatus.Idle("测试") with { State = state });
        using var gate = new GlobalLeakConfigurationGate();
        var service = new GlobalLeakSeatBlacklistService(settings, coordinator, gate, new ActivityLogService(),
            NullLogger<GlobalLeakSeatBlacklistService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>>()));
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task Save_SnapshotsMutableInput_AndPreservesConcurrentSettingsUpdate()
    {
        using var fixture = new GlobalLeakBlacklistExecutionTests.Fixture();
        await fixture.Gate.EnterAsync();
        var seats = new List<SeatReference> { new("a", "1") };
        var save = fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = seats });
        seats.Clear();
        await fixture.Settings.UpdateAsync(settings => settings with
        {
            Tasks = settings.Tasks with { GlobalLeak = settings.Tasks.GlobalLeak with
            { SelectedLibraries = [new(2, "新场馆", "5层")] } }
        });
        fixture.Gate.Exit();
        await save;
        Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats);
        Assert.Equal(2, Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.SelectedLibraries).LibraryId);
    }

    [Fact]
    public async Task CancelWaitingSave_DoesNotWriteOrLeakGate()
    {
        using var fixture = new GlobalLeakBlacklistExecutionTests.Fixture();
        await fixture.Gate.EnterAsync();
        using var cts = new CancellationTokenSource();
        var save = fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] }, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        fixture.Gate.Exit();
        Assert.Equal(0, fixture.Settings.SaveCalls);
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("b", "2")] });
        Assert.Equal("b", Assert.Single(fixture.Settings.CurrentSettings.Tasks.GlobalLeak.BlacklistedSeats).SeatKey);
    }
}
