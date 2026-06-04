using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITelegramAlertSender
{
    Task SendAsync(
        TelegramAlertSettings settings,
        string message,
        CancellationToken cancellationToken = default);
}
