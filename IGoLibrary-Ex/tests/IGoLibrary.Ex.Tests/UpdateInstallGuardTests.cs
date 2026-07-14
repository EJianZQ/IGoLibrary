using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateInstallGuardTests
{
    [Theory]
    [InlineData(CoordinatorTaskState.Starting)]
    [InlineData(CoordinatorTaskState.Running)]
    [InlineData(CoordinatorTaskState.Stopping)]
    public void GetBlockingTaskNames_ListsEveryActiveCoordinator(CoordinatorTaskState state)
    {
        var grab = new FakeGrabSeatCoordinator();
        var global = new FakeGlobalLeakCoordinator();
        var occupy = new FakeOccupySeatCoordinator();
        var tomorrow = new FakeTomorrowReservationCoordinator();
        grab.EmitStatus(Status(state, "抢座"));
        global.EmitStatus(Status(state, "全域捡漏"));
        occupy.EmitStatus(Status(state, "占座"));
        tomorrow.EmitStatus(Status(state, "明日预约"));
        var guard = new UpdateInstallGuard(grab, global, occupy, tomorrow);

        var result = guard.GetBlockingTaskNames();

        Assert.Equal(["抢座", "全域捡漏", "占座", "明日预约"], result);
    }

    [Fact]
    public void GetBlockingTaskNames_AllowsTerminalStates()
    {
        var guard = new UpdateInstallGuard(
            new FakeGrabSeatCoordinator(),
            new FakeGlobalLeakCoordinator(),
            new FakeOccupySeatCoordinator(),
            new FakeTomorrowReservationCoordinator());

        Assert.Empty(guard.GetBlockingTaskNames());
    }

    private static CoordinatorStatus Status(CoordinatorTaskState state, string name)
    {
        return new CoordinatorStatus(
            state,
            name,
            "test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            Reason: CoordinatorStatusReason.Running);
    }
}
