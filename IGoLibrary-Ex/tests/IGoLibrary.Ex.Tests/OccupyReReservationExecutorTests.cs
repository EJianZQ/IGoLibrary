using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class OccupyReReservationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Throws_WhenCancelFails()
    {
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(false)
        };
        var executor = new OccupyReReservationExecutor(
            apiClient,
            new ActivityLogService(),
            new FakeCoordinatorRuntime());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(
                "cookie",
                CreateReservation(),
                new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds),
                1,
                _ => { },
                CancellationToken.None));

        Assert.Equal("取消预约失败", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesUntilSuccess()
    {
        var cancelCallCount = 0;
        var reserveAttempts = 0;
        var activityLogService = new ActivityLogService();
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) =>
            {
                cancelCallCount++;
                return Task.FromResult(true);
            },
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveAttempts++;
                return Task.FromResult(reserveAttempts >= 2);
            }
        };
        var executor = new OccupyReReservationExecutor(
            apiClient,
            activityLogService,
            new FakeCoordinatorRuntime());

        var result = await executor.ExecuteAsync(
            "cookie",
            CreateReservation(),
            new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds),
            2,
            _ => { },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, cancelCallCount);
        Assert.Equal(2, reserveAttempts);
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Occupy" && entry.Message.Contains("第 1 次重新预约失败"));
        Assert.Contains(activityLogService.Entries, entry => entry.Category == "Occupy" && entry.Message.Contains("第 2 次重新预约尝试成功"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailed_WhenRetryLimitReached()
    {
        var reserveAttempts = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveAttempts++;
                return Task.FromResult(false);
            }
        };
        var executor = new OccupyReReservationExecutor(
            apiClient,
            new ActivityLogService(),
            new FakeCoordinatorRuntime());

        var result = await executor.ExecuteAsync(
            "cookie",
            CreateReservation(),
            new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds),
            2,
            _ => { },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(2, reserveAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesClassifiedGraphQlBusinessFailure_UntilSuccess()
    {
        var reserveAttempts = 0;
        var requestMessages = new List<string>();
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveAttempts++;
                return reserveAttempts == 1
                    ? Task.FromException<bool>(new InvalidOperationException("GraphQL 错误(code=1): 请重新尝试"))
                    : Task.FromResult(true);
            }
        };
        var executor = new OccupyReReservationExecutor(
            apiClient,
            new ActivityLogService(),
            new FakeCoordinatorRuntime());

        var result = await executor.ExecuteAsync(
            "cookie",
            CreateReservation(),
            new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds),
            2,
            requestMessages.Add,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, reserveAttempts);
        Assert.Equal(3, requestMessages.Count);
        Assert.Contains(requestMessages, message => message.Contains("取消"));
        Assert.Contains(requestMessages, message => message.Contains("第 2 次重新预约"));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotRetryUnclassifiedFailure()
    {
        var reserveAttempts = 0;
        var apiClient = new FakeTraceIntApiClient
        {
            OnCancelReservationAsync = (_, _, _) => Task.FromResult(true),
            OnReserveSeatAsync = (_, _, _, _) =>
            {
                reserveAttempts++;
                return Task.FromException<bool>(new InvalidOperationException("协议响应损坏"));
            }
        };
        var executor = new OccupyReReservationExecutor(
            apiClient,
            new ActivityLogService(),
            new FakeCoordinatorRuntime());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            "cookie",
            CreateReservation(),
            new OccupySeatPlan(TimeSpan.Zero, OccupyCheckIntervalMode.FixedTenSeconds),
            3,
            _ => { },
            CancellationToken.None));

        Assert.Equal("协议响应损坏", exception.Message);
        Assert.Equal(1, reserveAttempts);
    }

    private static ReservationInfo CreateReservation()
    {
        return new ReservationInfo(
            "token",
            1,
            "自科阅览区一",
            "seat-1",
            "1号座",
            DateTimeOffset.Now.AddSeconds(15));
    }
}
