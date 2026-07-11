using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Startup;

namespace IGoLibrary.Ex.Tests;

public sealed class SingleInstanceStartupContractTests
{
    [Fact]
    public void Run_WaitsThenAcquiresThenRuns_AndDisposesLeaseLast()
    {
        var events = new List<string>();
        var lease = new RecordingLease(() => events.Add("dispose"));
        var coordinator = new SingleInstanceStartupCoordinator(
            parentProcessId =>
            {
                Assert.Equal(42, parentProcessId);
                events.Add("wait");
                return Task.CompletedTask;
            },
            () =>
            {
                events.Add("acquire");
                return lease;
            },
            _ => events.Add("notice"),
            _ =>
            {
                Assert.False(lease.IsDisposed);
                events.Add("run");
            });

        coordinator.Run(new RestartArguments(42, ["--user-option"]));

        Assert.Equal(["wait", "acquire", "run", "dispose"], events);
        Assert.True(lease.IsDisposed);
    }

    [Fact]
    public void Run_ShowsDuplicateNotice_WithoutRunningPrimary_WhenLeaseIsUnavailable()
    {
        StartupNotice? shownNotice = null;
        var primaryStarted = false;
        var coordinator = new SingleInstanceStartupCoordinator(
            _ => Task.CompletedTask,
            () => null,
            notice => shownNotice = notice,
            _ => primaryStarted = true);

        coordinator.Run(new RestartArguments(null, []));

        Assert.Same(StartupNotice.DuplicateInstance, shownNotice);
        Assert.False(primaryStarted);
    }

    [Fact]
    public void Run_ShowsFailureNotice_WithoutAcquiringOrRunning_WhenParentWaitFails()
    {
        var acquireCalled = false;
        var primaryStarted = false;
        StartupNotice? shownNotice = null;
        var coordinator = new SingleInstanceStartupCoordinator(
            _ => Task.FromException(new TimeoutException("parent timeout")),
            () =>
            {
                acquireCalled = true;
                return null;
            },
            notice => shownNotice = notice,
            _ => primaryStarted = true);

        coordinator.Run(new RestartArguments(42, []));

        Assert.NotNull(shownNotice);
        Assert.Equal("启动失败", shownNotice.Title);
        Assert.Contains("parent timeout", shownNotice.Message, StringComparison.Ordinal);
        Assert.False(acquireCalled);
        Assert.False(primaryStarted);
    }

    [Fact]
    public void Run_DisposesLease_WhenPrimaryApplicationThrows()
    {
        var lease = new RecordingLease();
        var coordinator = new SingleInstanceStartupCoordinator(
            _ => Task.CompletedTask,
            () => lease,
            _ => { },
            _ => throw new InvalidOperationException("startup failed"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => coordinator.Run(new RestartArguments(null, [])));

        Assert.Equal("startup failed", exception.Message);
        Assert.True(lease.IsDisposed);
    }

    private sealed class RecordingLease(Action? onDispose = null) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            onDispose?.Invoke();
        }
    }
}
