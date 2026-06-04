using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IEmailAlertSender
{
    Task SendAsync(
        EmailAlertChannelSettings settings,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
