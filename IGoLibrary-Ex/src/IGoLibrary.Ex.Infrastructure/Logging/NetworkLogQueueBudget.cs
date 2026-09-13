namespace IGoLibrary.Ex.Infrastructure.Logging;

internal sealed class NetworkLogQueueBudget
{
    internal const long Capacity = 8 * 1024 * 1024;
    private long _used;
    internal long Used => Interlocked.Read(ref _used);

    public bool TryAcquire(int bytes)
    {
        while (true)
        {
            var used = Used;
            if (bytes > Capacity - used) return false;
            if (Interlocked.CompareExchange(ref _used, used + bytes, used) == used) return true;
        }
    }

    public void Release(int bytes) => Interlocked.Add(ref _used, -bytes);
}
