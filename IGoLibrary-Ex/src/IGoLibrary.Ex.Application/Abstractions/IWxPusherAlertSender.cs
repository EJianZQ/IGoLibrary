using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IWxPusherAlertSender
{
    Task SendAsync(
        WxPusherAlertChannelSettings settings,
        string title,
        string body,
        CancellationToken cancellationToken = default);
}
