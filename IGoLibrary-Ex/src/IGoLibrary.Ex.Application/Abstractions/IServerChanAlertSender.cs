using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IServerChanAlertSender
{
    Task SendAsync(
        ServerChanAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default);
}
