using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITelegramAlertSender
{
    Task SendAsync(
        TelegramAlertChannelSettings settings,
        string message,
        CancellationToken cancellationToken = default);
}
