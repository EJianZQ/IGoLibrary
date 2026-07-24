using System.Net;
using System.Text.RegularExpressions;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkServiceLoggingTests
{
    [Fact]
    public void SecurityAuditor_HashesSourceAndThrottlesRejectedRequests()
    {
        var logger = new CapturingLogger();
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero));
        var auditor = new NetworkRequestSecurityAuditor(logger, "测试服务", timeProvider);
        var context = CreateContext("bad-token");

        Assert.False(auditor.IsValidToken(context, "expected-token"));
        Assert.False(auditor.IsValidToken(context, "expected-token"));

        var first = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, first.Level);
        Assert.DoesNotContain("192.168.1.123", first.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("bad-token", first.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("expected-token", first.Message, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"来源哈希=[0-9A-F]{12}", RegexOptions.CultureInvariant),
            first.Message);
        Assert.Contains("本次合并数量=0", first.Message, StringComparison.Ordinal);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        Assert.False(auditor.IsValidToken(context, "expected-token"));

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("本次合并数量=1", logger.Entries[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeEventPublisher_IsolatesSubscriberFailureAndContinues()
    {
        var logger = new CapturingLogger();
        var expected = new InvalidOperationException("subscriber failed");
        var successfulSubscriberCalls = 0;
        EventHandler<EventArgs>? handlers = null;
        handlers += (_, _) => throw expected;
        handlers += (_, _) => successfulSubscriberCalls++;

        SafeEventPublisher.Publish(
            this,
            handlers,
            EventArgs.Empty,
            logger,
            "测试事件订阅者处理失败。");

        Assert.Equal(1, successfulSubscriberCalls);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Same(expected, warning.Exception);
    }

    private static DefaultHttpContext CreateContext(string token)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.123");
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/status";
        context.Request.QueryString = new QueryString($"?token={token}");
        return context;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
