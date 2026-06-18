using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IBarkAlertSender
{
    Task SendAsync(
        BarkAlertChannelSettings settings,
        string title,
        string message,
        CancellationToken cancellationToken = default);
}
