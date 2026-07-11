using IGoLibrary.Ex.Desktop.Startup;

namespace IGoLibrary.Ex.Tests;

public sealed class SingleInstanceLockTests
{
    [Fact]
    public void ScopeOptions_AreCurrentUserAndCrossSession()
    {
        var options = SingleInstanceLock.ScopeOptions;

        Assert.True(options.CurrentUserOnly);
        Assert.False(options.CurrentSessionOnly);
    }

    [Fact]
    public void TryAcquire_ReturnsLease_WhenNameIsAvailable()
    {
        using var lease = SingleInstanceLock.TryAcquire(CreateName());

        Assert.NotNull(lease);
    }

    [Fact]
    public void TryAcquire_DeniesSecondThread_AndAllowsReacquireAfterRelease()
    {
        var name = CreateName();
        using var firstAcquired = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        Exception? ownerFailure = null;
        var ownerHasLease = false;
        var ownerThread = new Thread(() =>
        {
            try
            {
                using var ownerLease = SingleInstanceLock.TryAcquire(name);
                ownerHasLease = ownerLease is not null;
                firstAcquired.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                ownerFailure = ex;
                firstAcquired.Set();
            }
        })
        {
            IsBackground = true
        };

        ownerThread.Start();
        Assert.True(firstAcquired.Wait(TimeSpan.FromSeconds(10)));

        try
        {
            Assert.Null(ownerFailure);
            Assert.True(ownerHasLease);
            using var duplicateLease = SingleInstanceLock.TryAcquire(name);
            Assert.Null(duplicateLease);
        }
        finally
        {
            releaseFirst.Set();
            Assert.True(ownerThread.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Null(ownerFailure);
        using var replacementLease = SingleInstanceLock.TryAcquire(name);
        Assert.NotNull(replacementLease);
    }

    [Fact]
    public async Task TryAcquire_DeniesSecondProcess_AndAllowsReacquireAfterProcessExit()
    {
        var name = CreateName();
        await using var ownerProcess = await TestChildProcess.StartAsync("hold-mutex", name);

        using var duplicateLease = SingleInstanceLock.TryAcquire(name);
        Assert.Null(duplicateLease);

        ownerProcess.Release();
        await ownerProcess.WaitForSuccessfulExitAsync();

        using var replacementLease = SingleInstanceLock.TryAcquire(name);
        Assert.NotNull(replacementLease);
    }

    [Fact]
    public void TryAcquire_TakesOwnershipOfAbandonedMutex()
    {
        var name = CreateName();
        Mutex? abandonedMutex = null;
        Exception? ownerFailure = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                abandonedMutex = new Mutex(false, name, SingleInstanceLock.ScopeOptions);
                abandonedMutex.WaitOne();
            }
            catch (Exception ex)
            {
                ownerFailure = ex;
            }
        });

        ownerThread.Start();
        Assert.True(ownerThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(ownerFailure);

        try
        {
            using var recoveredLease = SingleInstanceLock.TryAcquire(name);
            Assert.NotNull(recoveredLease);
        }
        finally
        {
            abandonedMutex?.Dispose();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryAcquire_RejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceLock.TryAcquire(name));
    }

    private static string CreateName()
        => $"IGoLibrary.Ex.Tests.SingleInstance.{Guid.NewGuid():N}";
}
