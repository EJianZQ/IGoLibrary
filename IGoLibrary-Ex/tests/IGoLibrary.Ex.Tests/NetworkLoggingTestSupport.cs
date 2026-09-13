using System.Collections.Concurrent;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

internal sealed class NetworkLogTestWriter : IAppLogWriter
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
    public string Text => string.Join("\n", Entries.Select(e => e.Message));
    public void Write(LogLevel level, string category, string message, Exception? exception = null,
        EventId eventId = default, DateTimeOffset? timestamp = null) => Entries.Enqueue((level, message));
    public void Flush() { }
}

internal sealed class NetworkLogTestContext
{
    public NetworkLogState State { get; } = new();
    public NetworkLogTestWriter Writer { get; } = new();
    public NetworkTrafficLogger Logger { get; }
    public NetworkLogTestContext(bool enabled = true)
    {
        State.Apply(enabled);
        Logger = new(State, Writer);
    }

    public HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new NetworkLoggingHandler(Logger, "测试") { InnerHandler = new NetworkTestHandler(send) });
}

internal sealed class NetworkTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
