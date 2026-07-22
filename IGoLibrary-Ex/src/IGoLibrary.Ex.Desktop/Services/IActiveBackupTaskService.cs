namespace IGoLibrary.Ex.Desktop.Services;

public interface IActiveBackupTaskService
{
    IReadOnlyList<string> GetActiveTaskNames();

    Task StopAllAsync(CancellationToken cancellationToken = default);
}
