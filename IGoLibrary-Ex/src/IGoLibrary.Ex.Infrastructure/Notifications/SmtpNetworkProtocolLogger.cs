using IGoLibrary.Ex.Infrastructure.Logging;
using MailKit;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

/// <summary>Captures server replies by operation; client SASL and MIME bytes are never captured.</summary>
internal sealed class SmtpNetworkProtocolLogger : IProtocolLogger
{
    private NetworkLogSession? _session;
    private NetworkBodyCapture? _reply;
    private bool _authentication;
    private bool _replyLineComplete;
    public IAuthenticationSecretDetector? AuthenticationSecretDetector { get; set; }

    public void Begin(NetworkLogSession? session, bool authentication)
    {
        _session = session;
        _authentication = authentication;
        _replyLineComplete = false;
        _reply = session is { IsActive: true } ? session.CreateBody() : null;
    }

    public void End(string stage, bool succeeded)
    {
        if (_reply is null) return;
        if (succeeded || _replyLineComplete) _reply.Complete(); else _reply.Fail();
        _session?.Write($"SMTP 阶段={stage}；服务器响应；{(_authentication ? "认证回复已隐藏" : _reply.Render("text/plain; charset=utf-8"))}");
        _reply = null;
    }

    public void LogConnect(Uri uri) { }
    public void LogClient(byte[] buffer, int offset, int count) { }
    public void LogServer(byte[] buffer, int offset, int count)
    {
        if (_authentication) return;
        try
        {
            _reply?.Append(buffer.AsSpan(offset, count));
            if (count > 0) _replyLineComplete = buffer[offset + count - 1] == (byte)'\n';
        }
        catch (Exception ex) { _session?.CaptureFailure(ex); }
    }
    public void Dispose() { _reply = null; }
}
