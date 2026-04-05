using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal interface ISmtpTransportClient : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default);

    Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default);

    Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default);
}
