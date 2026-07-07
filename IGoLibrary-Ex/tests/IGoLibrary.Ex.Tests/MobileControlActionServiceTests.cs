using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlActionServiceTests
{
    [Fact]
    public async Task CancelTaskAsync_WithActiveTasks_StopsMatchingCoordinator()
    {
        var grabCoordinator = new FakeGrabSeatCoordinator();
        var globalLeakCoordinator = new FakeGlobalLeakCoordinator();
        var tomorrowCoordinator = new FakeTomorrowReservationCoordinator();
        var occupyCoordinator = new FakeOccupySeatCoordinator();
        grabCoordinator.EmitStatus(CreateRunningStatus("抢座"));
        globalLeakCoordinator.EmitStatus(CreateRunningStatus("全域捡漏"));
        tomorrowCoordinator.EmitStatus(CreateRunningStatus("明日预约"));
        occupyCoordinator.EmitStatus(CreateRunningStatus("占座"));
        var service = CreateService(
            grabCoordinator: grabCoordinator,
            globalLeakCoordinator: globalLeakCoordinator,
            tomorrowCoordinator: tomorrowCoordinator,
            occupyCoordinator: occupyCoordinator);

        var grab = await service.CancelTaskAsync("grab");
        var globalLeak = await service.CancelTaskAsync("globalLeak");
        var tomorrow = await service.CancelTaskAsync("tomorrow");
        var occupy = await service.CancelTaskAsync("occupy");

        Assert.True(grab.Success);
        Assert.True(globalLeak.Success);
        Assert.True(tomorrow.Success);
        Assert.True(occupy.Success);
        Assert.Equal(1, grabCoordinator.StopCalls);
        Assert.Equal(1, globalLeakCoordinator.StopCalls);
        Assert.Equal(1, tomorrowCoordinator.StopCalls);
        Assert.Equal(1, occupyCoordinator.StopCalls);
    }

    [Fact]
    public async Task CancelTaskAsync_WhenTaskIsInactive_ReturnsConflictWithoutStopping()
    {
        var globalLeakCoordinator = new FakeGlobalLeakCoordinator();
        var service = CreateService(globalLeakCoordinator: globalLeakCoordinator);

        var result = await service.CancelTaskAsync("globalLeak");

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(0, globalLeakCoordinator.StopCalls);
    }

    [Fact]
    public async Task CancelTaskAsync_WithUnknownTaskKind_ReturnsBadRequest()
    {
        var service = CreateService();

        var result = await service.CancelTaskAsync("unknown");

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task CancelCurrentReservationAsync_WhenReservationExists_CallsWorkflowAndClearsRuntimeState()
    {
        var reservation = CreateReservation();
        var runtimeState = new AppRuntimeState
        {
            CurrentReservation = reservation
        };
        var workflowState = new ShellWorkflowState
        {
            CurrentReservation = reservation
        };
        var occupyCoordinator = new FakeOccupySeatCoordinator();
        occupyCoordinator.EmitStatus(CreateRunningStatus("占座"));
        var reservationWorkflow = new FakeReservationWorkflowService
        {
            CancelResult = new ReservationOperationResult(true, null)
        };
        var service = CreateService(
            runtimeState: runtimeState,
            workflowState: workflowState,
            occupyCoordinator: occupyCoordinator,
            reservationWorkflowService: reservationWorkflow);

        var result = await service.CancelCurrentReservationAsync();

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(1, reservationWorkflow.CancelCalls);
        Assert.Equal(reservation, reservationWorkflow.LastCancelledReservation);
        Assert.True(reservationWorkflow.LastStopOccupyFirst);
        Assert.Null(runtimeState.CurrentReservation);
        Assert.Null(workflowState.CurrentReservation);
    }

    [Fact]
    public async Task CancelCurrentReservationAsync_WhenNoReservation_ReturnsConflict()
    {
        var reservationWorkflow = new FakeReservationWorkflowService();
        var service = CreateService(reservationWorkflowService: reservationWorkflow);

        var result = await service.CancelCurrentReservationAsync();

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(0, reservationWorkflow.CancelCalls);
    }

    [Fact]
    public async Task CancelCurrentReservationAsync_WhenCancelAlreadyRunning_ReturnsConflictWithoutSecondWorkflowCall()
    {
        var reservation = CreateReservation();
        var runtimeState = new AppRuntimeState
        {
            CurrentReservation = reservation
        };
        var workflowState = new ShellWorkflowState
        {
            CurrentReservation = reservation
        };
        var reservationWorkflow = new FakeReservationWorkflowService
        {
            PendingCancelResult = new TaskCompletionSource<ReservationOperationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var service = CreateService(
            runtimeState: runtimeState,
            workflowState: workflowState,
            reservationWorkflowService: reservationWorkflow);

        var first = service.CancelCurrentReservationAsync();
        await reservationWorkflow.CancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = await service.CancelCurrentReservationAsync();
        reservationWorkflow.PendingCancelResult.SetResult(new ReservationOperationResult(true, null));
        var firstResult = await first;

        Assert.True(firstResult.Success);
        Assert.False(second.Success);
        Assert.Equal(StatusCodes.Status409Conflict, second.StatusCode);
        Assert.Equal(1, reservationWorkflow.CancelCalls);
    }

    private static MobileControlActionService CreateService(
        AppRuntimeState? runtimeState = null,
        ShellWorkflowState? workflowState = null,
        FakeGrabSeatCoordinator? grabCoordinator = null,
        FakeGlobalLeakCoordinator? globalLeakCoordinator = null,
        FakeTomorrowReservationCoordinator? tomorrowCoordinator = null,
        FakeOccupySeatCoordinator? occupyCoordinator = null,
        IReservationWorkflowService? reservationWorkflowService = null)
    {
        runtimeState ??= new AppRuntimeState();
        workflowState ??= new ShellWorkflowState();

        return new MobileControlActionService(
            grabCoordinator ?? new FakeGrabSeatCoordinator(),
            globalLeakCoordinator ?? new FakeGlobalLeakCoordinator(),
            tomorrowCoordinator ?? new FakeTomorrowReservationCoordinator(),
            occupyCoordinator ?? new FakeOccupySeatCoordinator(),
            reservationWorkflowService ?? new FakeReservationWorkflowService(),
            runtimeState,
            workflowState,
            new ActivityLogService());
    }

    private static CoordinatorStatus CreateRunningStatus(string title)
    {
        var now = DateTimeOffset.Now;
        return new CoordinatorStatus(
            CoordinatorTaskState.Running,
            title,
            $"{title}运行中。",
            now.AddSeconds(-10),
            now,
            Reason: CoordinatorStatusReason.Running);
    }

    private static ReservationInfo CreateReservation()
    {
        return new ReservationInfo(
            "reservation-token",
            1,
            "自科",
            "seat-1",
            "1",
            DateTimeOffset.Now.AddMinutes(30));
    }

    private sealed class FakeReservationWorkflowService : IReservationWorkflowService
    {
        public int CancelCalls { get; private set; }

        public ReservationInfo? LastCancelledReservation { get; private set; }

        public bool LastStopOccupyFirst { get; private set; }

        public ReservationOperationResult CancelResult { get; set; } =
            new(true, null);

        public TaskCompletionSource<object?> CancelStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ReservationOperationResult>? PendingCancelResult { get; set; }

        public Task<ReservationOperationResult> RefreshReservationAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ReservationOperationResult(true, null));
        }

        public Task<ReservationOperationResult> CancelCurrentReservationAsync(
            ReservationInfo reservation,
            bool stopOccupyFirst,
            CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            LastCancelledReservation = reservation;
            LastStopOccupyFirst = stopOccupyFirst;
            CancelStarted.TrySetResult(null);
            return PendingCancelResult?.Task ?? Task.FromResult(CancelResult);
        }
    }
}
