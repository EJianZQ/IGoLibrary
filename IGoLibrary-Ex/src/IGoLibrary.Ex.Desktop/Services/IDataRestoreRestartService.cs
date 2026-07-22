namespace IGoLibrary.Ex.Desktop.Services;

public interface IDataRestoreRestartService
{
    Task RestartAsync(string transactionId, CancellationToken cancellationToken = default);
}
