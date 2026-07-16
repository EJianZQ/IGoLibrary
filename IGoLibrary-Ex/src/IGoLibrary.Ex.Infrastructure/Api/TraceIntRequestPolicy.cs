using System.Net;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntRequestPolicy(
    ISettingsService settingsService,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

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

            await Task.Delay(
                TimeSpan.FromMilliseconds(250 * (attempt + 1)),
                _timeProvider,
                cancellationToken);
        }

        throw lastException ?? new InvalidOperationException($"{timeoutMessagePrefix}失败");
    }

    private async Task<(TimeSpan Timeout, int MaxRetries)> LoadNetworkSettingsAsync(CancellationToken cancellationToken)
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
