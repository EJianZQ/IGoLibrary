using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class SmtpEmailAlertSender(ISmtpTransportClientFactory transportClientFactory) : IEmailAlertSender
{
    public async Task SendAsync(
        CookieExpiryEmailAlertSettings settings,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var message = CreateMessage(settings, subject, body);
        await using var client = transportClientFactory.Create();

        await client.ConnectAsync(
            settings.SmtpHost,
            settings.Port,
            ResolveSocketOptions(settings),
            cancellationToken);

        if (ShouldAuthenticate(settings))
        {
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    internal static SecureSocketOptions ResolveSocketOptions(CookieExpiryEmailAlertSettings settings)
    {
        return settings.SecurityMode switch
        {
            EmailSecurityMode.None => SecureSocketOptions.None,
            EmailSecurityMode.Tls when settings.Port == 465 => SecureSocketOptions.SslOnConnect,
            EmailSecurityMode.Tls => SecureSocketOptions.StartTls,
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.SecurityMode, "Unsupported email security mode.")
        };
    }

    internal static MimeMessage CreateMessage(CookieExpiryEmailAlertSettings settings, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(settings.ToAddress));
        message.Headers.Replace(HeaderId.Subject, Encoding.UTF8, subject);
        var textPart = new TextPart("plain");
        textPart.SetText(Encoding.UTF8, body);
        message.Body = textPart;

        return message;
    }

    internal static bool ShouldAuthenticate(CookieExpiryEmailAlertSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.Username)
            && !string.IsNullOrWhiteSpace(settings.Password);
    }
}
