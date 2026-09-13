using System.Net.WebSockets;
using IGoLibrary.Ex.Infrastructure.Logging;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class LoggingQueueSocket(
    ITraceIntTomorrowReservationQueueSocket inner, NetworkTrafficLogger logger) : ITraceIntTomorrowReservationQueueSocket
{
    private NetworkLogSession? _session;
    private NetworkBodyCapture? _receiving;
    private long _sent;
    private long _received;
    private int _closed;
    public WebSocketState State => inner.State;
    public void SetRequestHeader(string name, string value) => inner.SetRequestHeader(name, value);

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        _session = logger.Start("明日预约队列", "出站", "WebSocket", uri.ToString());
        try { await inner.ConnectAsync(uri, cancellationToken); _session?.Write("连接成功"); }
        catch (Exception ex) { _session?.Failure(ex); throw; }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        try
        {
            await inner.SendAsync(payload, cancellationToken);
            if (_session is { IsActive: true } session)
            {
                var body = session.CreateBody();
                body.Append(payload.Span);
                body.Complete();
                session.Write($"发送消息；序号={Interlocked.Increment(ref _sent)}；{body.Render("application/json")}");
            }
        }
        catch (Exception ex) { _session?.Failure(ex); throw; }
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            var result = await inner.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) RecordClosed();
            else if (_session is { IsActive: true } session)
            {
                _receiving ??= session.CreateBody();
                _receiving.Append(buffer.AsSpan(0, result.Count));
                if (result.EndOfMessage)
                {
                    _receiving.Complete();
                    session.Write($"接收消息；序号={++_received}；{_receiving.Render(result.MessageType == WebSocketMessageType.Text ? "text/plain; charset=utf-8" : "application/octet-stream")}");
                    _receiving = null;
                }
            }
            return result;
        }
        catch (Exception ex) { _receiving?.Fail(); _session?.Failure(ex); throw; }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        try { await inner.CloseAsync(cancellationToken); }
        catch (Exception ex) { _session?.Failure(ex); throw; }
        finally { RecordClosed(); }
    }

    private void RecordClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        if (_receiving is not null) _session?.Write($"接收消息结束；{_receiving.Render("text/plain")}");
        _session?.Write("连接关闭");
    }

    public void Abort() { try { inner.Abort(); } finally { RecordClosed(); } }
    public void Dispose() { try { inner.Dispose(); } finally { RecordClosed(); } }
}
