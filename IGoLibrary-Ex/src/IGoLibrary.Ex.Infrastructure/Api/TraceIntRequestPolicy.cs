using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntRequestPolicy(
    ISettingsService settingsService,
    TimeProvider? timeProvider = null,
    ILogger<TraceIntRequestPolicy>? logger = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<TraceIntRequestPolicy> _logger = logger ?? NullLogger<TraceIntRequestPolicy>.Instance;
    private readonly object _settingsFailureGate = new();
    private DateTimeOffset _lastSettingsFailureLoggedAt = DateTimeOffset.MinValue;

    public async Task<T> ExecuteOnceAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string timeoutMessagePrefix,
        CancellationToken cancellationToken)
    {
        var settings = await LoadNetworkSettingsAsync(cancellationToken);
        using var timeoutCts = new CancellationTokenSource(settings.Timeout, _timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await operation(linkedCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "TraceInt 操作超时。操作={Operation}，超时秒数={TimeoutSeconds}，是否启用重试=否。",
                timeoutMessagePrefix,
                settings.Timeout.TotalSeconds);
            throw new TimeoutException(
                $"{timeoutMessagePrefix}超时（>{settings.Timeout.TotalSeconds:0} 秒）",
                ex);
        }
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string timeoutMessagePrefix,
        CancellationToken cancellationToken)
    {
        var settings = await LoadNetworkSettingsAsync(cancellationToken);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(settings.Timeout, _timeProvider);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await operation(linkedCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"{timeoutMessagePrefix}超时（>{settings.Timeout.TotalSeconds:0} 秒）",
                    ex);
            }
            catch (HttpRequestException ex) when (IsTransient(ex.StatusCode))
            {
                lastException = ex;
            }

            if (attempt >= settings.MaxRetries)
            {
                break;
            }

            var retryDelay = TimeSpan.FromMilliseconds(250 * (attempt + 1));
            _logger.LogWarning(
                "TraceInt 操作将在退避后重试。操作={Operation}，当前尝试={Attempt}，最大尝试次数={MaxAttempts}，延迟毫秒={DelayMs}，失败类型={FailureType}，HTTP 状态={HttpStatus}。",
                timeoutMessagePrefix,
                attempt + 1,
                settings.MaxRetries + 1,
                retryDelay.TotalMilliseconds,
                lastException?.GetType().Name ?? "未知",
                (lastException as HttpRequestException)?.StatusCode);
            await Task.Delay(
                retryDelay,
                _timeProvider,
                cancellationToken);
        }

        var terminalException = lastException ?? new InvalidOperationException($"{timeoutMessagePrefix}失败");
        _logger.LogWarning(
            terminalException,
            "TraceInt 操作在重试后仍失败。操作={Operation}，尝试次数={Attempts}。",
            timeoutMessagePrefix,
            settings.MaxRetries + 1);
        throw terminalException;
    }

    private async Task<(TimeSpan Timeout, int MaxRetries)> LoadNetworkSettingsAsync(CancellationToken cancellationToken)
    {
        NetworkRequestSettings settings;
        try
        {
            settings = (await settingsService.LoadAsync(cancellationToken)).Network;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            settings = NetworkRequestSettings.Default;
            var now = _timeProvider.GetUtcNow();
            var shouldLog = false;
            lock (_settingsFailureGate)
            {
                if (now - _lastSettingsFailureLoggedAt >= TimeSpan.FromMinutes(1))
                {
                    _lastSettingsFailureLoggedAt = now;
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                _logger.LogWarning(ex, "读取网络请求设置失败，TraceInt 请求暂时使用默认超时与重试设置。");
            }
        }

        var timeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 1, 60);
        var maxRetries = Math.Clamp(settings.MaxRetries, 0, 10);
        return (TimeSpan.FromSeconds(timeoutSeconds), maxRetries);
    }

    internal static bool IsTransient(HttpStatusCode? statusCode)
    {
        return statusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int?)statusCode >= 500;
    }
}
