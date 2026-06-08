using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntTomorrowReservationQueueTransport
{
    internal static readonly TimeSpan DefaultSendInterval = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan DefaultSuccessSettleDelay = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly byte[] QueuePayloadBytes = Encoding.UTF8.GetBytes("""{"ns":"prereserve/queue","msg":""}""");
    private static readonly string[] StopKeywords =
    [
        "不在",
        "未开始",
        "结束",
        "已闭馆",
        "登记了",
        "已登记"
    ];
    private static readonly string[] ContinueKeywords =
    [
        "ok",
        "排队成功",
        "u6392",
        "您已经预定了座位",
        "u6210",
        "不需要排队"
    ];
    private readonly Func<ITraceIntTomorrowReservationQueueSocket> _socketFactory;
    private readonly TimeSpan _sendInterval;
    private readonly TimeSpan _maxWait;
    private readonly TimeSpan _successSettleDelay;
    private readonly TimeSpan _cleanupTimeout;

    public TraceIntTomorrowReservationQueueTransport()
        : this(
            static () => new ClientWebSocketAdapter(),
            DefaultSendInterval,
            DefaultMaxWait,
            DefaultSuccessSettleDelay,
            DefaultCleanupTimeout)
    {
    }

    internal TraceIntTomorrowReservationQueueTransport(
        Func<ITraceIntTomorrowReservationQueueSocket> socketFactory,
        TimeSpan sendInterval,
        TimeSpan maxWait,
        TimeSpan successSettleDelay,
        TimeSpan cleanupTimeout)
    {
        _socketFactory = socketFactory;
        _sendInterval = sendInterval;
        _maxWait = maxWait;
        _successSettleDelay = successSettleDelay;
        _cleanupTimeout = cleanupTimeout;
    }

    public async Task<TomorrowReservationQueueResult> EnterAsync(
        string queueUrl,
        string cookie,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(queueUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("ws" or "wss"))
        {
            return TomorrowReservationQueueResult.Continue("明日预约排队地址无效，继续 HTTP 预约");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_maxWait);

        try
        {
            using var socket = _socketFactory();
            socket.SetRequestHeader("Cookie", cookie);
            socket.SetRequestHeader("Origin", TraceIntGraphQlTransport.TomorrowReservationProfile.Origin);
            socket.SetRequestHeader("User-Agent", TraceIntGraphQlTransport.TomorrowReservationProfile.UserAgent);

            await socket.ConnectAsync(uri, timeoutCts.Token);
            await socket.SendAsync(QueuePayloadBytes, timeoutCts.Token);

            var resultCompletion = new TaskCompletionSource<TomorrowReservationQueueResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var receiveTask = ReceiveLoopAsync(socket, resultCompletion, timeoutCts.Token);
            var sendTask = SendLoopAsync(socket, timeoutCts.Token);

            var timeoutTask = Task.Delay(_maxWait, cancellationToken);
            var completed = await Task.WhenAny(resultCompletion.Task, timeoutTask);

            timeoutCts.Cancel();
            await CloseSocketSafelyAsync(socket, _cleanupTimeout);
            await ObserveTaskSafelyAsync(receiveTask, _cleanupTimeout);
            await ObserveTaskSafelyAsync(sendTask, _cleanupTimeout);

            if (completed == resultCompletion.Task)
            {
                var result = await resultCompletion.Task;
                if (!result.ShouldStop)
                {
                    await Task.Delay(_successSettleDelay, cancellationToken);
                }

                return result;
            }

            if (completed == timeoutTask && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return TomorrowReservationQueueResult.Continue("明日预约排队通道 15 秒内未明确拦截，继续 HTTP 预约");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TomorrowReservationQueueResult.Continue($"明日预约排队通道连接异常，继续 HTTP 预约：{ex.Message}");
        }
    }

    private async Task SendLoopAsync(
        ITraceIntTomorrowReservationQueueSocket socket,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               socket.State is WebSocketState.Open)
        {
            await Task.Delay(_sendInterval, cancellationToken);
            if (socket.State is not WebSocketState.Open)
            {
                return;
            }

            await socket.SendAsync(QueuePayloadBytes, cancellationToken);
        }
    }

    private static async Task ReceiveLoopAsync(
        ITraceIntTomorrowReservationQueueSocket socket,
        TaskCompletionSource<TomorrowReservationQueueResult> resultCompletion,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested &&
               socket.State is WebSocketState.Open)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            var message = builder.ToString();
            var classified = ClassifyQueueMessage(message);
            if (classified is not null)
            {
                resultCompletion.TrySetResult(classified);
                return;
            }
        }
    }

    internal static TomorrowReservationQueueResult? ClassifyQueueMessage(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return null;
        }

        var message = ExtractQueueMessage(rawMessage);
        var normalized = $"{message}\n{rawMessage}".ToLowerInvariant();

        if (StopKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return TomorrowReservationQueueResult.Stop(message);
        }

        if (ContinueKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return TomorrowReservationQueueResult.Continue($"明日预约排队通道返回：{message}");
        }

        return null;
    }

    private static string ExtractQueueMessage(string rawMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("msg", out var msgElement) &&
                msgElement.ValueKind == JsonValueKind.String)
            {
                return msgElement.GetString() ?? rawMessage;
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? rawMessage;
            }
        }
        catch (JsonException)
        {
        }

        return rawMessage;
    }

    private static async Task CloseSocketSafelyAsync(
        ITraceIntTomorrowReservationQueueSocket socket,
        TimeSpan cleanupTimeout)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
            var closeTask = socket.CloseAsync(cleanupCts.Token);
            var completed = await Task.WhenAny(closeTask, Task.Delay(cleanupTimeout));
            if (completed == closeTask)
            {
                try
                {
                    await closeTask;
                }
                catch
                {
                    socket.Abort();
                }

                return;
            }

            cleanupCts.Cancel();
            socket.Abort();
        }
        catch
        {
            socket.Abort();
        }
    }

    private static async Task ObserveTaskSafelyAsync(Task task, TimeSpan cleanupTimeout)
    {
        try
        {
            if (!task.IsCompleted)
            {
                var completed = await Task.WhenAny(task, Task.Delay(cleanupTimeout));
                if (completed != task)
                {
                    return;
                }
            }

            await task;
        }
        catch
        {
        }
    }
}

internal interface ITraceIntTomorrowReservationQueueSocket : IDisposable
{
    WebSocketState State { get; }

    void SetRequestHeader(string name, string value);

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);

    void Abort();
}

internal sealed class ClientWebSocketAdapter : ITraceIntTomorrowReservationQueueSocket
{
    private readonly ClientWebSocket _socket = new();

    public WebSocketState State => _socket.State;

    public void SetRequestHeader(string name, string value)
    {
        _socket.Options.SetRequestHeader(name, value);
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        return _socket.ConnectAsync(uri, cancellationToken);
    }

    public Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        return _socket.SendAsync(
            payload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).AsTask();
    }

    public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        return _socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        return _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
    }

    public void Abort()
    {
        _socket.Abort();
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
}
