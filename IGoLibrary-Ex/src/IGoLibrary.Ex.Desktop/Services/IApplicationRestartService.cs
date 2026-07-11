namespace IGoLibrary.Ex.Desktop.Services;

public interface IApplicationRestartService
{
    Task RestartAsync(CancellationToken cancellationToken = default);
}
