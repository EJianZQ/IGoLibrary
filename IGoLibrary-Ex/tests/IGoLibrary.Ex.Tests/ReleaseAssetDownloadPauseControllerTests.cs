using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class ReleaseAssetDownloadPauseControllerTests
{
    [Fact]
    public async Task PauseAndResume_AreIdempotentAndReleaseWaiters()
    {
        using var controller = new ReleaseAssetDownloadPauseController();
        var firstToken = controller.PauseToken;

        Assert.True(controller.TryPause());
        Assert.False(controller.TryPause());
        Assert.True(controller.IsPaused);
        Assert.True(firstToken.IsCancellationRequested);
        var waiter = controller.WaitWhilePausedAsync().AsTask();
        Assert.False(waiter.IsCompleted);

        Assert.True(controller.TryResume());
        Assert.False(controller.TryResume());
        await waiter;
        Assert.False(controller.IsPaused);
        Assert.False(controller.PauseToken.IsCancellationRequested);
    }

    [Fact]
    public async Task WaitWhileRunning_CompletesImmediately()
    {
        using var controller = new ReleaseAssetDownloadPauseController();

        await controller.WaitWhilePausedAsync();

        Assert.False(controller.IsPaused);
    }
}
