using System.Net;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NotificationRequestPolicyCancellationTests
{
    [Fact]
    public async Task RequestPolicy_WhenSettingsLoadIsCanceled_DoesNotInvokeRequest()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operationCalls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HttpNotificationRequestPolicy.ExecuteAsync(
                settingsService,
                "测试通道",
                _ =>
                {
                    operationCalls++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                },
                TimeProvider.System,
                cancellation.Token));

        Assert.Equal(0, operationCalls);
    }

    [Fact]
    public async Task RequestPolicy_ThrottlesRepeatedSettingsLoadFailureLogs()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        settingsService.LoadExceptions.Enqueue(new InvalidOperationException("settings unavailable 1"));
        settingsService.LoadExceptions.Enqueue(new InvalidOperationException("settings unavailable 2"));
        settingsService.LoadExceptions.Enqueue(new InvalidOperationException("settings unavailable 3"));
        var timeProvider = new FakeTimeProvider(
            new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero));
        var logger = new CapturingLogger();

        for (var index = 0; index < 2; index++)
        {
            using var response = await HttpNotificationRequestPolicy.ExecuteAsync(
                settingsService,
                "测试通道",
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                timeProvider,
                CancellationToken.None,
                logger);
        }

        Assert.Single(logger.SettingsFailureEntries);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using var laterResponse = await HttpNotificationRequestPolicy.ExecuteAsync(
            settingsService,
            "测试通道",
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            timeProvider,
            CancellationToken.None,
            logger);

        Assert.Equal(2, logger.SettingsFailureEntries.Count);
    }

    [Theory]
    [InlineData(NotificationSenderKind.Bark, "Bark")]
    [InlineData(NotificationSenderKind.Telegram, "Telegram")]
    [InlineData(NotificationSenderKind.WxPusher, "WxPusher")]
    [InlineData(NotificationSenderKind.ServerChan, "Server酱")]
    public async Task SendAsync_DistinguishesInternalTimeoutFromCallerCancellation(
        NotificationSenderKind senderKind,
        string requestLabel)
    {
        await AssertInternalTimeoutAsync(senderKind, requestLabel);
        await AssertCallerCancellationAsync(senderKind);
    }

    private static async Task AssertInternalTimeoutAsync(
        NotificationSenderKind senderKind,
        string requestLabel)
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(requestStarted);
        var timeProvider = new FakeTimeProvider();

        var sendTask = SendAsync(senderKind, handler, timeProvider, CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => sendTask);
        Assert.Equal($"{requestLabel}请求超时（>1 秒）", exception.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.Equal(1, handler.CallCount);
    }

    private static async Task AssertCallerCancellationAsync(NotificationSenderKind senderKind)
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = CreateBlockingHandler(requestStarted);
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();

        var sendTask = SendAsync(senderKind, handler, timeProvider, cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
        Assert.Equal(1, handler.CallCount);
    }

    private static SequenceHttpMessageHandler CreateBlockingHandler(TaskCompletionSource requestStarted)
    {
        return new SequenceHttpMessageHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
    }

    private static Task SendAsync(
        NotificationSenderKind senderKind,
        HttpMessageHandler handler,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Network = new NetworkRequestSettings(1, 0)
        });

        return senderKind switch
        {
            NotificationSenderKind.Bark => new BarkAlertSender(httpClient, settingsService, timeProvider).SendAsync(
                new BarkAlertChannelSettings(true, "https://api.day.app", "key-1", "", "", ""),
                "测试标题",
                "测试内容",
                cancellationToken),
            NotificationSenderKind.Telegram => new TelegramAlertSender(httpClient, settingsService, timeProvider).SendAsync(
                new TelegramAlertChannelSettings(true, "https://api.telegram.org", "token-1", "chat-1"),
                "测试内容",
                cancellationToken),
            NotificationSenderKind.WxPusher => new WxPusherAlertSender(httpClient, settingsService, timeProvider).SendAsync(
                new WxPusherAlertChannelSettings(true, "https://wxpusher.zjiecode.com", "AT_xxx", "UID_xxx", ""),
                "测试标题",
                "测试内容",
                cancellationToken),
            NotificationSenderKind.ServerChan => new ServerChanAlertSender(httpClient, settingsService, timeProvider).SendAsync(
                new ServerChanAlertChannelSettings(true, "SCT_xxx", false, "", ""),
                "测试标题",
                "测试内容",
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(senderKind), senderKind, null)
        };
    }

    public enum NotificationSenderKind
    {
        Bark,
        Telegram,
        WxPusher,
        ServerChan
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> SettingsFailureEntries
            => _entries
                .Where(entry => entry.Message.Contains("读取网络请求设置失败", StringComparison.Ordinal))
                .ToArray();

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
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

public sealed class TraceIntRequestPolicyCancellationTests
{
    [Fact]
    public async Task RequestPolicy_WhenSettingsLoadIsCanceled_DoesNotInvokeRequest()
    {
        var settingsService = new FakeSettingsService(AppSettings.Default);
        var policy = new TraceIntRequestPolicy(settingsService);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var operationCalls = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync(
                _ =>
                {
                    operationCalls++;
                    return Task.FromResult(0);
                },
                "TraceInt 测试请求",
                cancellation.Token));

        Assert.Equal(0, operationCalls);
    }

    [Fact]
    public async Task RequestPolicy_DistinguishesInternalTimeoutFromCallerCancellation()
    {
        var timeProvider = new FakeTimeProvider();
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Network = new NetworkRequestSettings(1, 2)
        });
        var policy = new TraceIntRequestPolicy(settingsService, timeProvider);

        var timeoutStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutTask = policy.ExecuteOnceAsync(
            token => WaitForCancellationAsync(timeoutStarted, token),
            "TraceInt 单次请求",
            CancellationToken.None);
        await timeoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var timeout = await Assert.ThrowsAsync<TimeoutException>(() => timeoutTask);
        Assert.Equal("TraceInt 单次请求超时（>1 秒）", timeout.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);

        using var onceCancellation = new CancellationTokenSource();
        var onceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onceTask = policy.ExecuteOnceAsync(
            token => WaitForCancellationAsync(onceStarted, token),
            "TraceInt 单次请求",
            onceCancellation.Token);
        await onceStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        onceCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => onceTask);

        using var retryingCancellation = new CancellationTokenSource();
        var retryingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executeCalls = 0;
        var retryingTask = policy.ExecuteAsync(
            token =>
            {
                executeCalls++;
                return WaitForCancellationAsync(retryingStarted, token);
            },
            "TraceInt 重试请求",
            retryingCancellation.Token);
        await retryingStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        retryingCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retryingTask);
        Assert.Equal(1, executeCalls);
    }

    private static async Task<int> WaitForCancellationAsync(
        TaskCompletionSource requestStarted,
        CancellationToken cancellationToken)
    {
        requestStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
