using System.Net;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal static class HttpNotificationRequestPolicy
{
    public static async Task<HttpResponseMessage> ExecuteAsync(
        ISettingsService settingsService,
        string requestLabel,
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var settings = await LoadNetworkSettingsAsync(settingsService, cancellationToken);
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

            await Task.Delay(
                TimeSpan.FromMilliseconds(250 * (attempt + 1)),
                timeProvider,
                cancellationToken);
        }

        throw lastException ?? new InvalidOperationException($"{requestLabel}请求失败");
    }

    private static async Task<(TimeSpan Timeout, int MaxRetries)> LoadNetworkSettingsAsync(
        ISettingsService settingsService,
        CancellationToken cancellationToken)
    {
        NetworkRequestSettings settings;
        try
        {
            settings = (await settingsService.LoadAsync(cancellationToken)).Network;
        }
        catch
        {
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
}
