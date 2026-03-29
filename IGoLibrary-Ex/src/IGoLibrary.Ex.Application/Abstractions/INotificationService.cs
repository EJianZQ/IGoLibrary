namespace IGoLibrary.Ex.Application.Abstractions;

public interface INotificationService
{
    Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default);

    Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default);

    Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default);
}
