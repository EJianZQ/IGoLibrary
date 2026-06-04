using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class GrabReservationAttemptStrategyTests
{
    [Fact]
    public async Task DirectReserve_DoesNotLoadLayout()
    {
        var layoutCallCount = 0;
        var requestCount = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                throw new InvalidOperationException("直接预约策略不应加载布局。");
            },
            OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true)
        };
        var strategy = new DirectReserveGrabReservationStrategy(apiClient, new ActivityLogService(), new FakeCoordinatorRuntime());

        var result = await strategy.TryReserveAsync(
            CreateContext([new SeatReference("seat-1", "1号座")], () => requestCount++),
            CancellationToken.None);

        Assert.Equal(0, layoutCallCount);
        Assert.Equal(1, requestCount);
        Assert.True(result.HadReservationAttempt);
        Assert.Equal("seat-1", result.ReservedSeat?.SeatKey);
    }

    [Fact]
    public async Task DirectReserve_ContinuesToNextSeat_WhenSeatIsOccupied()
    {
        var reserveRequests = new List<string>();
        var activityLogService = new ActivityLogService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveRequests.Add(seatKey);
                return seatKey == "seat-1"
                    ? Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 该座位已经被人预定了!"))
                    : Task.FromResult(true);
            }
        };
        var strategy = new DirectReserveGrabReservationStrategy(apiClient, activityLogService, new FakeCoordinatorRuntime());

        var result = await strategy.TryReserveAsync(
            CreateContext(
                [
                    new SeatReference("seat-1", "1号座"),
                    new SeatReference("seat-2", "2号座")
                ]),
            CancellationToken.None);

        Assert.Equal(new[] { "seat-1", "seat-2" }, reserveRequests);
        Assert.Equal("seat-2", result.ReservedSeat?.SeatKey);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Grab" && entry.Message.Contains("继续尝试下一个目标座位"));
    }

    [Fact]
    public async Task DirectReserve_ReturnsRateLimitSignal_WhenApiAsksToRetry()
    {
        var reserveRequests = new List<string>();
        var apiClient = new FakeTraceIntApiClient
        {
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveRequests.Add(seatKey);
                return Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 请重新尝试"));
            }
        };
        var strategy = new DirectReserveGrabReservationStrategy(apiClient, new ActivityLogService(), new FakeCoordinatorRuntime());

        var result = await strategy.TryReserveAsync(
            CreateContext(
                [
                    new SeatReference("seat-1", "1号座"),
                    new SeatReference("seat-2", "2号座")
                ]),
            CancellationToken.None);

        Assert.Equal(new[] { "seat-1" }, reserveRequests);
        Assert.True(result.HadReservationAttempt);
        Assert.True(result.RateLimitTriggered);
        Assert.Equal(1, result.NextSeatStartIndex);
    }

    [Fact]
    public async Task QueryThenReserve_LoadsLayout_ReservesFirstAvailableTargetSeat_AndReturnsLatestLayout()
    {
        var layoutCallCount = 0;
        var reserveRequests = new List<string>();
        var requestCount = 0;
        var layout = new LibraryLayout(
            1,
            "自科阅览区一",
            "3层",
            true,
            10,
            0,
            0,
            [
                new SeatSnapshot("seat-1", "1号座", false, 0, 0),
                new SeatSnapshot("seat-2", "2号座", true, 1, 0),
                new SeatSnapshot("seat-3", "3号座", false, 2, 0)
            ]);
        var apiClient = new FakeTraceIntApiClient
        {
            OnGetLibraryLayoutAsync = (_, _, _) =>
            {
                layoutCallCount++;
                return Task.FromResult(layout);
            },
            OnReserveSeatAsync = (_, _, seatKey, _) =>
            {
                reserveRequests.Add(seatKey);
                return Task.FromResult(true);
            }
        };
        var strategy = new QueryThenReserveGrabReservationStrategy(apiClient, new ActivityLogService());

        var result = await strategy.TryReserveAsync(
            CreateContext(
                [
                    new SeatReference("seat-1", "1号座"),
                    new SeatReference("seat-3", "3号座")
                ],
                () => requestCount++),
            CancellationToken.None);

        Assert.Equal(1, layoutCallCount);
        Assert.Equal(2, requestCount);
        Assert.Equal(new[] { "seat-1" }, reserveRequests);
        Assert.Same(layout, result.LatestLayout);
        Assert.Equal("seat-1", result.ReservedSeat?.SeatKey);
    }

    [Fact]
    public void Selector_ReturnsStrategyForConfiguredMode()
    {
        var apiClient = new FakeTraceIntApiClient();
        var activityLogService = new ActivityLogService();
        var queryStrategy = new QueryThenReserveGrabReservationStrategy(apiClient, activityLogService);
        var directStrategy = new DirectReserveGrabReservationStrategy(apiClient, activityLogService, new FakeCoordinatorRuntime());
        var selector = new GrabReservationStrategySelector([queryStrategy, directStrategy]);

        Assert.Same(queryStrategy, selector.Select(GrabReservationStrategy.QueryThenReserve));
        Assert.Same(directStrategy, selector.Select(GrabReservationStrategy.ReserveDirectly));
    }

    private static GrabReservationAttemptContext CreateContext(
        IReadOnlyList<SeatReference> seats,
        Action? markRequestSent = null)
    {
        return new GrabReservationAttemptContext(
            "cookie",
            new GrabSeatPlan(
                1,
                "自科阅览区一",
                seats,
                GrabPollingMode.Aggressive,
                GrabPollingStrategyFactory.FromMode(GrabPollingMode.Aggressive),
                null),
            0,
            markRequestSent ?? (() => { }));
    }
}
