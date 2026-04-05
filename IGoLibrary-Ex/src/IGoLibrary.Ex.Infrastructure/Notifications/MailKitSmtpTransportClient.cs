using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class MailKitSmtpTransportClient : ISmtpTransportClient
{
    private readonly SmtpClient _client = new();

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        => _client.ConnectAsync(host, port, options, cancellationToken);

    public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
        => _client.AuthenticateAsync(username, password, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
        => _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        => _client.DisconnectAsync(quit, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
