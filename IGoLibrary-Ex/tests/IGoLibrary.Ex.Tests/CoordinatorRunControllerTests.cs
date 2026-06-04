using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class CoordinatorRunControllerTests
{
    [Fact]
    public async Task StartAsync_Throws_WhenTaskIsAlreadyRunning()
    {
        var runtime = new FakeCoordinatorRuntime();
        var controller = new CoordinatorRunController("测试", runtime);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await controller.StartAsync(async (context, _) =>
        {
            context.SetRunning("运行中");
            await release.Task;
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Running);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.StartAsync((_, _) => Task.CompletedTask));
        Assert.Contains("任务已在运行", ex.Message);

        release.SetResult(null);
        await WaitForStatusAsync(controller, CoordinatorTaskState.Completed);
    }

    [Fact]
    public async Task StopAsync_ReturnsNoOp_WhenTaskIsIdleOrTerminal()
    {
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime());

        await controller.StopAsync();
        Assert.Equal(CoordinatorTaskState.Idle, controller.GetStatus().State);

        await controller.StartAsync((context, _) =>
        {
            context.Complete("已完成", CoordinatorStatusReason.TaskFailed);
            return Task.CompletedTask;
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Completed);

        await controller.StopAsync();
        Assert.Equal(CoordinatorTaskState.Completed, controller.GetStatus().State);
        Assert.Equal(CoordinatorStatusReason.TaskFailed, controller.GetStatus().Reason);
    }

    [Fact]
    public async Task StopAsync_CompletesRunningTask_WithStoppedReason()
    {
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime());

        await controller.StartAsync(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Starting);

        await controller.StopAsync();

        var status = controller.GetStatus();
        Assert.Equal(CoordinatorTaskState.Completed, status.State);
        Assert.Equal(CoordinatorStatusReason.Stopped, status.Reason);
    }

    [Fact]
    public async Task StopAsync_ReportsStoppingBeforeStoppedTerminal()
    {
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime());
        var states = new List<CoordinatorTaskState>();
        controller.StatusChanged += (_, status) => states.Add(status.State);

        await controller.StartAsync(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        await controller.StopAsync();

        Assert.Equal(
            [CoordinatorTaskState.Starting, CoordinatorTaskState.Stopping, CoordinatorTaskState.Completed],
            states);
    }

    [Fact]
    public async Task StartAsync_AllowsRestart_AfterFailedTerminalStatus()
    {
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime());
        var attempts = 0;

        await controller.StartAsync((_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("第一次失败");
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Failed);

        await controller.StartAsync((context, _) =>
        {
            attempts++;
            context.Complete("第二次完成", CoordinatorStatusReason.Stopped);
            return Task.CompletedTask;
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Completed);

        Assert.Equal(2, attempts);
        Assert.Equal("第二次完成", controller.GetStatus().Message);
    }

    [Fact]
    public async Task StatusChanged_ReportsStartingRunningAndTerminal_InOrder()
    {
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime());
        var states = new List<CoordinatorTaskState>();
        controller.StatusChanged += (_, status) => states.Add(status.State);

        await controller.StartAsync((context, _) =>
        {
            context.SetRunning("运行中");
            context.Complete("已停止", CoordinatorStatusReason.Stopped);
            return Task.CompletedTask;
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Completed);

        Assert.Equal(
            [CoordinatorTaskState.Starting, CoordinatorTaskState.Running, CoordinatorTaskState.Completed],
            states);
    }

    private static async Task WaitForStatusAsync(
        CoordinatorRunController controller,
        CoordinatorTaskState expectedState)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (controller.GetStatus().State == expectedState)
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Expected status {expectedState} was not observed.");
    }
}
