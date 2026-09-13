using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.Notifications;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingProtocolTests
{
    [Fact]
    public async Task WebSocketCapturesCompleteUtf8MessagesAndEachSend()
    {
        var logs = new NetworkLogTestContext();
        var bytes = Encoding.UTF8.GetBytes("中文排队消息");
        using var inner = new QueueSocket(bytes);
        using var socket = new LoggingQueueSocket(inner, logs.Logger);
        socket.SetRequestHeader("Cookie", "private-cookie");
        await socket.ConnectAsync(new Uri("wss://a.test/queue?token=private-token"), default);
        await socket.SendAsync("{\"msg\":\"queue\"}"u8.ToArray(), default);
        await socket.SendAsync("{\"msg\":\"queue\"}"u8.ToArray(), default);
        var buffer = new byte[1];
        for (var i = 0; i < bytes.Length; i++) Assert.Equal(bytes[i], (await ReceiveOne()).Byte);
        await socket.CloseAsync(default);
        socket.Dispose();
        Assert.Equal(2, inner.Sends);
        Assert.Contains("中文排队消息", logs.Writer.Text);
        Assert.Equal(2, logs.Writer.Entries.Count(e => e.Message.Contains("发送消息")));
        Assert.Single(logs.Writer.Entries, e => e.Message.Contains("接收消息；"));
        Assert.Single(logs.Writer.Entries, e => e.Message.Contains("连接关闭"));
        Assert.DoesNotContain("private-cookie", logs.Writer.Text);
        Assert.DoesNotContain("private-token", logs.Writer.Text);
        async Task<(byte Byte, bool End)> ReceiveOne()
        {
            var result = await socket.ReceiveAsync(buffer, default);
            return (buffer[0], result.EndOfMessage);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmtpRealLoopbackCapturesServerRepliesAndRedactedMail(bool authenticate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeSmtpAsync(listener, cancellation.Token);
        var logs = new NetworkLogTestContext();
        try
        {
            await using var client = new MailKitSmtpTransportClient(logs.Logger);
            await client.ConnectAsync("127.0.0.1", port, SecureSocketOptions.None, cancellation.Token);
            if (authenticate) await client.AuthenticateAsync("private-user", "private-password", cancellation.Token);
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse("private-sender@example.com"));
            message.To.Add(MailboxAddress.Parse("private-recipient@example.com"));
            message.Subject = "预约通知";
            message.Body = new TextPart("plain") { Text = "座位 A001\npassword=private-password" };
            await client.SendAsync(message, cancellation.Token);
            await client.DisconnectAsync(true, cancellation.Token);
            await server.WaitAsync(cancellation.Token);
            Assert.Contains("A001", logs.Writer.Text);
            Assert.Contains("250 queued", logs.Writer.Text);
            Assert.DoesNotContain("private-password", logs.Writer.Text);
            Assert.DoesNotContain("private-user", logs.Writer.Text);
            Assert.DoesNotContain("private-sender", logs.Writer.Text);
            Assert.DoesNotContain("private-recipient", logs.Writer.Text);
            Assert.DoesNotContain("Content-Transfer-Encoding", logs.Writer.Text);
        }
        finally { cancellation.Cancel(); listener.Stop(); try { await server; } catch (OperationCanceledException) { } }
    }

    private static async Task ServeSmtpAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost ready".AsMemory(), cancellationToken);
        var data = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (data)
            {
                if (line != ".") continue;
                data = false;
                await writer.WriteLineAsync("250 queued".AsMemory(), cancellationToken);
            }
            else if (line.StartsWith("EHLO"))
                await writer.WriteAsync("250-localhost\r\n250 AUTH PLAIN\r\n".AsMemory(), cancellationToken);
            else if (line.StartsWith("AUTH")) await writer.WriteLineAsync("235 authenticated".AsMemory(), cancellationToken);
            else if (line == "DATA") { data = true; await writer.WriteLineAsync("354 send data".AsMemory(), cancellationToken); }
            else if (line == "QUIT") { await writer.WriteLineAsync("221 bye".AsMemory(), cancellationToken); break; }
            else await writer.WriteLineAsync("250 ok".AsMemory(), cancellationToken);
        }
    }

    private sealed class QueueSocket(byte[] bytes) : ITraceIntTomorrowReservationQueueSocket
    {
        private int _offset;
        public int Sends { get; private set; }
        public WebSocketState State { get; private set; }
        public void SetRequestHeader(string name, string value) { }
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) { State = WebSocketState.Open; return Task.CompletedTask; }
        public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) { Sends++; return Task.CompletedTask; }
        public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            buffer[0] = bytes[_offset++];
            return Task.FromResult(new WebSocketReceiveResult(1, WebSocketMessageType.Text, _offset == bytes.Length));
        }
        public Task CloseAsync(CancellationToken cancellationToken) { State = WebSocketState.Closed; return Task.CompletedTask; }
        public void Abort() => State = WebSocketState.Aborted;
        public void Dispose() => State = WebSocketState.Closed;
    }
}
