using System.Text.RegularExpressions;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

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

        var ex = await Assert.ThrowsAsync<TaskLaunchConflictException>(() =>
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

    [Fact]
    public async Task TerminalSubscriberFailure_LogKeepsTheCompletingRunId()
    {
        var writer = new CollectingLogWriter();
        var controller = new CoordinatorRunController("测试", new FakeCoordinatorRuntime(), writer);
        controller.StatusChanged += (_, status) =>
        {
            if (status.State == CoordinatorTaskState.Completed)
            {
                throw new InvalidOperationException("terminal subscriber failed");
            }
        };

        await controller.StartAsync((context, _) =>
        {
            context.Complete("完成", CoordinatorStatusReason.Stopped);
            return Task.CompletedTask;
        });
        await WaitForStatusAsync(controller, CoordinatorTaskState.Completed);
        await WaitForLogAsync(writer, 2006);

        var startingMessage = Assert.Single(
            writer.Entries,
            entry => entry.EventId.Id == 2001).Message;
        var subscriberFailureMessage = Assert.Single(
            writer.Entries,
            entry => entry.EventId.Id == 2006).Message;
        var runId = Regex.Match(startingMessage, @"运行标识=([0-9a-f]{32})").Groups[1].Value;
        Assert.NotEmpty(runId);
        Assert.Contains($"运行标识={runId}", subscriberFailureMessage, StringComparison.Ordinal);
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

    private static async Task WaitForLogAsync(CollectingLogWriter writer, int eventId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (writer.Entries.Any(entry => entry.EventId.Id == eventId))
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Expected log event {eventId} was not observed.");
    }

    private sealed class CollectingLogWriter : IAppLogWriter
    {
        private readonly object _gate = new();
        private readonly List<(string Message, EventId EventId)> _entries = [];

        public IReadOnlyList<(string Message, EventId EventId)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public void Write(
            LogLevel level,
            string category,
            string message,
            Exception? exception = null,
            EventId eventId = default,
            DateTimeOffset? timestamp = null)
        {
            lock (_gate)
            {
                _entries.Add((message, eventId));
            }
        }

        public void Flush()
        {
        }
    }
}
