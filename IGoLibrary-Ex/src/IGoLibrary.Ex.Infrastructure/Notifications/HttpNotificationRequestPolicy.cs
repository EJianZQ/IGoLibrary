using System.Net;
using System.Runtime.CompilerServices;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal static class HttpNotificationRequestPolicy
{
    private static readonly ConditionalWeakTable<ISettingsService, SettingsFailureLogState>
        SettingsFailureLogStates = new();

    public static async Task<HttpResponseMessage> ExecuteAsync(
        ISettingsService settingsService,
        string requestLabel,
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var settings = await LoadNetworkSettingsAsync(
            settingsService,
            timeProvider,
            cancellationToken,
            logger);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
        {
            using var timeoutCts = new CancellationTokenSource(settings.Timeout, timeProvider);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                var response = await operation(linkedCts.Token);
                if (!IsTransient(response.StatusCode))
                {
                    return response;
                }

                lastException = new HttpRequestException(
                    $"{requestLabel}请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}",
                    null,
                    response.StatusCode);
                response.Dispose();
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                lastException = new TimeoutException($"{requestLabel}请求超时（>{settings.Timeout.TotalSeconds:0} 秒）", ex);
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
            logger?.LogWarning(
                "通知请求将在退避后重试。通道={Channel}，当前尝试={Attempt}，最大尝试次数={MaxAttempts}，延迟毫秒={DelayMs}，失败类型={FailureType}，HTTP 状态={HttpStatus}。",
                requestLabel,
                attempt + 1,
                settings.MaxRetries + 1,
                retryDelay.TotalMilliseconds,
                lastException?.GetType().Name ?? "未知",
                (lastException as HttpRequestException)?.StatusCode);
            await Task.Delay(
                retryDelay,
                timeProvider,
                cancellationToken);
        }

        var terminalException = lastException ?? new InvalidOperationException($"{requestLabel}请求失败");
        logger?.LogWarning(
            terminalException,
            "通知请求在重试后仍失败。通道={Channel}，尝试次数={Attempts}。",
            requestLabel,
            settings.MaxRetries + 1);
        throw terminalException;
    }

    private static async Task<(TimeSpan Timeout, int MaxRetries)> LoadNetworkSettingsAsync(
        ISettingsService settingsService,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ILogger? logger)
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
            if (logger is not null &&
                SettingsFailureLogStates
                    .GetValue(settingsService, static _ => new SettingsFailureLogState())
                    .ShouldLog(timeProvider.GetUtcNow()))
            {
                logger.LogWarning(ex, "读取网络请求设置失败，通知请求暂时使用默认超时与重试设置。");
            }

            settings = NetworkRequestSettings.Default;
        }

        var timeoutSeconds = Math.Clamp(settings.TimeoutSeconds, 1, 60);
        var maxRetries = Math.Clamp(settings.MaxRetries, 0, 10);
        return (TimeSpan.FromSeconds(timeoutSeconds), maxRetries);
    }

    private static bool IsTransient(HttpStatusCode? statusCode)
    {
        return statusCode is null
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || (int?)statusCode >= 500;
    }

    private sealed class SettingsFailureLogState
    {
        private readonly object _gate = new();
        private DateTimeOffset _lastLoggedAt = DateTimeOffset.MinValue;

        public bool ShouldLog(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (now - _lastLoggedAt < TimeSpan.FromMinutes(1))
                {
                    return false;
                }

                _lastLoggedAt = now;
                return true;
            }
        }
    }
}
