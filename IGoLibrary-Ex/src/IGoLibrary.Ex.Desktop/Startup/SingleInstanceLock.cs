namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed class SingleInstanceLock : IDisposable
{
    internal const string DefaultName = "IGoLibrary.Ex.Desktop.SingleInstance.v1";

    private readonly Mutex _mutex;
    private int _disposed;

    private SingleInstanceLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    internal static NamedWaitHandleOptions ScopeOptions => new()
    {
        CurrentUserOnly = true,
        CurrentSessionOnly = false
    };

    public static SingleInstanceLock? TryAcquire(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(false, name, ScopeOptions);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            return ownsMutex ? new SingleInstanceLock(mutex) : null;
        }
        finally
        {
            if (!ownsMutex)
            {
                mutex.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
