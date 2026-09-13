using System.Text;
using IGoLibrary.Ex.Infrastructure.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class MailKitSmtpTransportClient : ISmtpTransportClient
{
    private readonly SmtpNetworkProtocolLogger _protocol = new();
    private readonly SmtpClient _client;
    private readonly NetworkTrafficLogger? _logger;
    private NetworkLogSession? _session;

    public MailKitSmtpTransportClient(NetworkTrafficLogger? logger = null)
    {
        _logger = logger;
        _client = new SmtpClient(_protocol);
    }

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
    {
        _session = _logger?.Start("邮件通知", "出站", "SMTP", $"smtp://{host}:{port}");
        return RunAsync("连接", () => _client.ConnectAsync(host, port, options, cancellationToken), cancellationToken);
    }

    public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
        => RunAsync("认证", () => _client.AuthenticateAsync(username, password, cancellationToken), cancellationToken, authentication: true);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        if (_session is { IsActive: true } session)
        {
            try
            {
                var capture = session.CreateBody();
                var text = $"主题={message.Subject}\n发件人={message.From}\n收件人={message.To}\n{message.TextBody}";
                var bytes = new byte[4096];
                var encoder = Encoding.UTF8.GetEncoder();
                var remaining = text.AsSpan();
                do
                {
                    encoder.Convert(remaining, bytes, true, out var used, out var written, out _);
                    capture.Append(bytes.AsSpan(0, written));
                    remaining = remaining[used..];
                } while (!remaining.IsEmpty);
                capture.Complete();
                session.Write($"SMTP 邮件请求体；{capture.Render("text/plain; charset=utf-8")}");
            }
            catch (Exception ex) { session.CaptureFailure(ex); }
        }
        return RunAsync("发送邮件", async () => { await _client.SendAsync(message, cancellationToken); }, cancellationToken);
    }

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        => RunAsync("断开", () => _client.DisconnectAsync(quit, cancellationToken), cancellationToken);

    private async Task RunAsync(string stage, Func<Task> action, CancellationToken cancellationToken, bool authentication = false)
    {
        _session?.Write($"SMTP 阶段={stage}；请求={(authentication ? "认证载荷已隐藏" : stage)}");
        _protocol.Begin(_session, authentication);
        var succeeded = false;
        try { await action(); succeeded = true; }
        catch (Exception ex) { _session?.Failure(ex); throw; }
        finally { _protocol.End(stage, succeeded); }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _session?.Write("SMTP 会话结束");
        return ValueTask.CompletedTask;
    }
}
