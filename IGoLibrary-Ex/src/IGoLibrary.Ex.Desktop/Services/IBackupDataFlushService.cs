namespace IGoLibrary.Ex.Desktop.Services;

public interface IBackupDataFlushService
{
    void Configure(Func<CancellationToken, Task> flushAsync);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
