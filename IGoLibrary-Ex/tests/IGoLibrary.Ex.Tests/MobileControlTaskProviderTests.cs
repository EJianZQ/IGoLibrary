using Avalonia.Headless.XUnit;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class MobileControlTaskProviderTests
{
    [Fact]
    public async Task TaskRecordsProvider_MapsDisplayFieldsAndPreservesOrder()
    {
        var now = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        var firstRecordedAt = now.AddMinutes(-1);
        var secondRecordedAt = now.AddMinutes(-2);
        var history = new StubHistoryService
        {
            GrabRecords =
            [
                new GrabTaskLaunchRecord(
                    "11111111111111111111111111111111",
                    firstRecordedAt,
                    1,
                    "电子阅览室A<&>",
                    [new SeatReference("seat-27", "27号"), new SeatReference("seat-38", "38号")],
                    GrabPollingMode.Randomized,
                    GrabReservationStrategy.ReserveDirectly),
                new GrabTaskLaunchRecord(
                    "22222222222222222222222222222222",
                    secondRecordedAt,
                    2,
                    "第二场馆",
                    [new SeatReference("seat-1", "1号")],
                    GrabPollingMode.Aggressive,
                    GrabReservationStrategy.QueryThenReserve)
            ],
            GlobalLeakRecords =
            [
                new GlobalLeakTaskLaunchRecord(
                    "33333333333333333333333333333333",
                    firstRecordedAt,
                    [
                        new GlobalLeakLibraryTarget(9, "高优先级", "3层"),
                        new GlobalLeakLibraryTarget(3, "低优先级", "1层")
                    ],
                    TimeSpan.FromSeconds(17.5))
            ]
        };
        var provider = new MobileControlTaskRecordsProvider(
            history,
            new FakeTimeProvider(now),
            NullLogger<MobileControlTaskRecordsProvider>.Instance);

        var snapshot = await provider.CreateSnapshotAsync();

        Assert.Equal(now, snapshot.GeneratedAt);
        Assert.Equal(["11111111111111111111111111111111", "22222222222222222222222222222222"],
            snapshot.Grab.Select(static record => record.RecordId));
        Assert.Equal(firstRecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), snapshot.Grab[0].RecordedAtText);
        Assert.Equal("电子阅览室A<&>", snapshot.Grab[0].LibraryName);
        Assert.Equal(["27号", "38号"], snapshot.Grab[0].SeatNames);
        Assert.Equal("随机延迟", snapshot.Grab[0].PollingModeText);
        Assert.Equal("直接发送预约请求", snapshot.Grab[0].ReservationStrategyText);
        Assert.Equal("极限速度", snapshot.Grab[1].PollingModeText);
        Assert.Equal("先获取列表判断状态", snapshot.Grab[1].ReservationStrategyText);
        Assert.Equal([9, 3], history.GlobalLeakRecords[0].Libraries.Select(static library => library.LibraryId));
        Assert.Equal(["高优先级", "低优先级"], snapshot.GlobalLeak[0].Libraries.Select(static library => library.LibraryName));
        Assert.Equal(17.5, snapshot.GlobalLeak[0].ScanIntervalSeconds);
    }

    [AvaloniaFact]
    public async Task OccupyPlanProvider_ReadsCurrentViewModelValuesAndAppliesFactoryValidation()
    {
        var viewModel = new OccupyPageViewModel(
            new FakeOccupySeatCoordinator(),
            new FakeTaskLaunchService(),
            new FakeReservationWorkflowService(),
            new ActivityLogService(),
            new FakeNotificationService(),
            new FakeTimeProvider());
        var provider = new MobileControlOccupyPlanProvider(viewModel);
        viewModel.ReReserveDelaySeconds = 0;
        viewModel.SelectedOccupyCheckIntervalModeIndex = (int)OccupyCheckIntervalMode.RandomTenToTwentySeconds;

        var plan = await provider.CreatePlanAsync();

        Assert.Equal(TimeSpan.FromSeconds(1), plan.ReReserveDelay);
        Assert.Equal(OccupyCheckIntervalMode.RandomTenToTwentySeconds, plan.OccupyCheckIntervalMode);
    }

    private sealed class StubHistoryService : ITaskLaunchHistoryService
    {
        public IReadOnlyList<GrabTaskLaunchRecord> GrabRecords { get; init; } = [];

        public IReadOnlyList<GlobalLeakTaskLaunchRecord> GlobalLeakRecords { get; init; } = [];

        public Task<IReadOnlyList<GrabTaskLaunchRecord>> GetRecentGrabAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GrabRecords);

        public Task<IReadOnlyList<GlobalLeakTaskLaunchRecord>> GetRecentGlobalLeakAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GlobalLeakRecords);

        public Task<GrabTaskLaunchRecord?> GetGrabAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GrabRecords.SingleOrDefault(record => record.RecordId == recordId));

        public Task<GlobalLeakTaskLaunchRecord?> GetGlobalLeakAsync(string recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GlobalLeakRecords.SingleOrDefault(record => record.RecordId == recordId));

        public Task<TaskLaunchHistorySaveResult> RecordGrabAsync(GrabSeatPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskLaunchHistorySaveResult> RecordGlobalLeakAsync(GlobalLeakPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
