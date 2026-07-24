using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using Microsoft.Extensions.Hosting;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class CookieExpirationAlertHostedService(
    CookieExpirationAlertMonitor monitor,
    IActivityLogService activityLogService,
    TimeProvider timeProvider) : BackgroundService
{
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);
    private string? _lastFailureSignature;
    private DateTimeOffset _lastFailureLoggedAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitor.CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogFailureWithRateLimit(ex);
            }

            try
            {
                await Task.Delay(CheckInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void LogFailureWithRateLimit(Exception exception)
    {
        var now = timeProvider.GetLocalNow();
        var signature = $"{exception.GetType().FullName}|{exception.Message}";
        if (string.Equals(signature, _lastFailureSignature, StringComparison.Ordinal) &&
            now - _lastFailureLoggedAt < FailureLogInterval)
        {
            return;
        }

        _lastFailureSignature = signature;
        _lastFailureLoggedAt = now;
        activityLogService.Write(
            LogEntryKind.Warning,
            "Alert",
            $"Cookie 到期提醒后台监测失败，将自动重试：{exception.Message}",
            exception);
    }
}
