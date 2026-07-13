using Avalonia.Controls;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMainWindowSizePersistenceService
{
    Task InitializeAsync(Window window, CancellationToken cancellationToken = default);

    void SetRememberSizeEnabled(bool enabled, bool captureCurrentSize);

    Task FlushAsync(CancellationToken cancellationToken = default);
}

internal sealed class NoOpMainWindowSizePersistenceService : IMainWindowSizePersistenceService
{
    public static NoOpMainWindowSizePersistenceService Instance { get; } = new();

    private NoOpMainWindowSizePersistenceService()
    {
    }

    public Task InitializeAsync(Window window, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void SetRememberSizeEnabled(bool enabled, bool captureCurrentSize)
    {
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
