using IGoLibrary.Ex.Desktop.Startup;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class RestartParentProcessWaiterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldWait_ReturnsFalse_ForMissingOrInvalidProcessId(int? processId)
    {
        Assert.False(RestartParentProcessWaiter.ShouldWait(processId));
    }

    [Fact]
    public void ShouldWait_ReturnsFalse_ForCurrentProcess()
    {
        Assert.False(RestartParentProcessWaiter.ShouldWait(Environment.ProcessId));
    }

    [Fact]
    public void ShouldWait_ReturnsTrue_ForOtherPositiveProcessId()
    {
        var otherProcessId = Environment.ProcessId == int.MaxValue
            ? Environment.ProcessId - 1
            : Environment.ProcessId + 1;

        Assert.True(RestartParentProcessWaiter.ShouldWait(otherProcessId));
    }

    [Fact]
    public async Task WaitForExitAsync_Returns_WhenProcessNoLongerExists()
    {
        await RestartParentProcessWaiter.WaitForExitAsync(int.MaxValue);
    }

    [Fact]
    public async Task WaitForExitAsync_RemainsPendingUntilLiveProcessExits()
    {
        await using var child = await TestChildProcess.StartAsync("wait-for-release");

        var waitTask = RestartParentProcessWaiter.WaitForExitAsync(
            child.Id,
            TimeSpan.FromSeconds(5));
        Assert.False(waitTask.IsCompleted);

        child.Release();
        await child.WaitForSuccessfulExitAsync();
        await waitTask;
    }

    [Fact]
    public async Task WaitForExitAsync_ThrowsTimeout_WhenLiveProcessDoesNotExit()
    {
        await using var child = await TestChildProcess.StartAsync("wait-for-release");

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            RestartParentProcessWaiter.WaitForExitAsync(
                child.Id,
                TimeSpan.FromMilliseconds(50)));

        Assert.Contains(child.Id.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForExitAsync_PreservesCallerCancellation()
    {
        await using var child = await TestChildProcess.StartAsync("wait-for-release");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RestartParentProcessWaiter.WaitForExitAsync(
                child.Id,
                TimeSpan.FromSeconds(5),
                cancellation.Token));
    }
}
