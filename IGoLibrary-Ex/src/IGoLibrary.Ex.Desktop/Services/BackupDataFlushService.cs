namespace IGoLibrary.Ex.Desktop.Services;

public sealed class BackupDataFlushService : IBackupDataFlushService
{
    private readonly object _gate = new();
    private Func<CancellationToken, Task> _flushAsync = static _ => Task.CompletedTask;

    public void Configure(Func<CancellationToken, Task> flushAsync)
    {
        ArgumentNullException.ThrowIfNull(flushAsync);
        lock (_gate)
        {
            _flushAsync = flushAsync;
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task> flush;
        lock (_gate)
        {
            flush = _flushAsync;
        }

        return flush(cancellationToken);
    }
}
