using System.Net.WebSockets;
using System.Text;
using IGoLibrary.Ex.Infrastructure.Api;

namespace IGoLibrary.Ex.Tests;

public sealed class TomorrowReservationQueueTransportTests
{
    [Fact]
    public void ClassifyQueueMessage_ReturnsStop_WhenMessageContainsInterceptKeyword()
    {
        var result = TraceIntTomorrowReservationQueueTransport.ClassifyQueueMessage("""{"msg":"当前不在预约时间内"}""");

        Assert.NotNull(result);
        Assert.True(result.ShouldStop);
        Assert.Contains("不在", result.Message);
    }

    [Fact]
    public void ClassifyQueueMessage_ReturnsContinue_WhenMessageContainsSuccessKeyword()
    {
        var result = TraceIntTomorrowReservationQueueTransport.ClassifyQueueMessage("""{"msg":"排队成功"}""");

        Assert.NotNull(result);
        Assert.False(result.ShouldStop);
        Assert.Contains("排队成功", result.Message);
    }

    [Fact]
    public void ClassifyQueueMessage_ReturnsNull_WhenMessageIsNotActionable()
    {
        var result = TraceIntTomorrowReservationQueueTransport.ClassifyQueueMessage("heartbeat");

        Assert.Null(result);
    }

    [Fact]
    public void DefaultTimings_MatchOfficialQueueStrategy()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), TraceIntTomorrowReservationQueueTransport.DefaultSendInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), TraceIntTomorrowReservationQueueTransport.DefaultMaxWait);
        Assert.Equal(TimeSpan.FromMilliseconds(500), TraceIntTomorrowReservationQueueTransport.DefaultCleanupTimeout);
    }

    [Fact]
    public async Task EnterAsync_SendsImmediately_AndContinues_WhenQueueSucceeds()
    {
        var socket = new FakeQueueSocket
        {
            ReceiveMessages = new Queue<string>(["""{"msg":"排队成功"}"""])
        };
        var transport = CreateTransport(socket);

        var result = await transport.EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie");

        Assert.False(result.ShouldStop);
        Assert.Contains("排队成功", result.Message);
        Assert.Equal("""{"ns":"prereserve/queue","msg":""}""", Assert.Single(socket.SentMessages));
        Assert.Equal("cookie", socket.Headers["Cookie"]);
        Assert.Equal("https://web.traceint.com", socket.Headers["Origin"]);
    }

    [Fact]
    public async Task EnterAsync_Stops_WhenQueueIntercepts()
    {
        var socket = new FakeQueueSocket
        {
            ReceiveMessages = new Queue<string>(["""{"msg":"当前未开始预约"}"""])
        };
        var transport = CreateTransport(socket);

        var result = await transport.EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie");

        Assert.True(result.ShouldStop);
        Assert.Contains("未开始", result.Message);
    }

    [Fact]
    public async Task EnterAsync_Continues_WhenConnectFails()
    {
        var socket = new FakeQueueSocket
        {
            ConnectException = new WebSocketException("connect failed")
        };
        var transport = CreateTransport(socket);

        var result = await transport.EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie");

        Assert.False(result.ShouldStop);
        Assert.Contains("连接异常", result.Message);
        Assert.Contains("connect failed", result.Message);
    }

    [Fact]
    public async Task EnterAsync_ContinuesAfterTimeout_AndSendsOnConfiguredInterval()
    {
        var socket = new FakeQueueSocket();
        var transport = CreateTransport(
            socket,
            sendInterval: TimeSpan.FromMilliseconds(5),
            maxWait: TimeSpan.FromMilliseconds(35));

        var result = await transport.EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie");

        Assert.False(result.ShouldStop);
        Assert.Contains("未明确拦截", result.Message);
        Assert.True(socket.SentMessages.Count >= 2);
        Assert.All(socket.SentMessages, message =>
            Assert.Equal("""{"ns":"prereserve/queue","msg":""}""", message));
    }

    [Fact]
    public async Task EnterAsync_Aborts_WhenCloseDoesNotComplete()
    {
        var socket = new FakeQueueSocket
        {
            ReceiveMessages = new Queue<string>(["""{"msg":"排队成功"}"""]),
            CloseNeverCompletes = true
        };
        var transport = CreateTransport(
            socket,
            cleanupTimeout: TimeSpan.FromMilliseconds(5));

        var result = await transport
            .EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie")
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.ShouldStop);
        Assert.Equal(1, socket.AbortCalls);
    }

    [Fact]
    public async Task EnterAsync_DoesNotWaitForReceiveTask_WhenTimeoutCleanupCannotFinish()
    {
        var socket = new FakeQueueSocket
        {
            IgnoreReceiveCancellation = true
        };
        var transport = CreateTransport(
            socket,
            sendInterval: TimeSpan.FromMilliseconds(5),
            maxWait: TimeSpan.FromMilliseconds(15),
            cleanupTimeout: TimeSpan.FromMilliseconds(5));

        var result = await transport
            .EnterAsync("wss://wechat.v2.traceint.com/ws?ns=prereserve/queue", "cookie")
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.ShouldStop);
        Assert.Contains("未明确拦截", result.Message);
    }

    private static TraceIntTomorrowReservationQueueTransport CreateTransport(
        FakeQueueSocket socket,
        TimeSpan? sendInterval = null,
        TimeSpan? maxWait = null,
        TimeSpan? cleanupTimeout = null)
    {
        return new TraceIntTomorrowReservationQueueTransport(
            () => socket,
            sendInterval ?? TimeSpan.FromMilliseconds(5),
            maxWait ?? TimeSpan.FromSeconds(1),
            successSettleDelay: TimeSpan.Zero,
            cleanupTimeout ?? TimeSpan.FromMilliseconds(50));
    }

    private sealed class FakeQueueSocket : ITraceIntTomorrowReservationQueueSocket
    {
        public Dictionary<string, string> Headers { get; } = [];

        public Queue<string> ReceiveMessages { get; init; } = new();

        public List<string> SentMessages { get; } = [];

        public Exception? ConnectException { get; init; }

        public bool CloseNeverCompletes { get; init; }

        public bool IgnoreReceiveCancellation { get; init; }

        public int AbortCalls { get; private set; }

        public WebSocketState State { get; private set; } = WebSocketState.None;

        public void SetRequestHeader(string name, string value)
        {
            Headers[name] = value;
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            SentMessages.Add(Encoding.UTF8.GetString(payload.Span));
            return Task.CompletedTask;
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync(
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            if (ReceiveMessages.Count == 0)
            {
                if (IgnoreReceiveCancellation)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var payload = Encoding.UTF8.GetBytes(ReceiveMessages.Dequeue());
            payload.CopyTo(buffer, 0);
            return new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, endOfMessage: true);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            if (CloseNeverCompletes)
            {
                return Task.Delay(Timeout.InfiniteTimeSpan);
            }

            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            AbortCalls++;
            State = WebSocketState.Aborted;
        }

        public void Dispose()
        {
            State = WebSocketState.Closed;
        }
    }
}
