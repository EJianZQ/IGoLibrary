using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Application.Services;

public sealed class CookieExpirationAlertMonitor(
    ISessionState sessionState,
    ITaskEventAlertDispatcher alertDispatcher,
    IActivityLogService activityLogService,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan AlertWindow = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan DisabledOrFailedRetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NearExpirationRetryInterval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly HashSet<string> _notifiedCookieFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unparseableCookieFingerprints = new(StringComparer.Ordinal);
    private string? _observedCookieFingerprint;
    private DateTimeOffset? _observedExpirationTime;
    private DateTimeOffset? _nextDispatchAttemptAt;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        await _checkGate.WaitAsync(cancellationToken);
        try
        {
            var cookie = sessionState.Session?.Cookie;
            if (string.IsNullOrWhiteSpace(cookie))
            {
                ClearCurrentObservation();
                return;
            }

            var fingerprint = ComputeFingerprint(cookie);
            ObserveCookie(cookie, fingerprint);
            if (_observedExpirationTime is not { } expirationTime ||
                _observedCookieFingerprint is null)
            {
                return;
            }

            var now = timeProvider.GetLocalNow();
            var remaining = expirationTime - now;
            if (remaining <= TimeSpan.Zero || remaining > AlertWindow ||
                _notifiedCookieFingerprints.Contains(fingerprint) ||
                (_nextDispatchAttemptAt is { } nextAttemptAt && now < nextAttemptAt))
            {
                return;
            }

            bool accepted;
            try
            {
                accepted = await alertDispatcher.TryNotifyCookieExpiringAsync(
                    expirationTime,
                    remaining,
                    cancellationToken);
            }
            catch
            {
                ScheduleNextDispatchAttempt(now, remaining);
                throw;
            }

            if (!accepted)
            {
                ScheduleNextDispatchAttempt(now, remaining);
                return;
            }

            _notifiedCookieFingerprints.Add(fingerprint);
            _nextDispatchAttemptAt = null;
            activityLogService.Write(
                LogEntryKind.Success,
                "Alert",
                $"已受理 Cookie 即将到期提醒，到期时间：{expirationTime:yyyy-MM-dd HH:mm:ss zzz}。");
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private void ObserveCookie(string cookie, string fingerprint)
    {
        if (string.Equals(_observedCookieFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _observedCookieFingerprint = fingerprint;
        _nextDispatchAttemptAt = null;

        if (!SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime))
        {
            _observedExpirationTime = null;
            if (_unparseableCookieFingerprints.Add(fingerprint))
            {
                activityLogService.Write(
                    LogEntryKind.Warning,
                    "Alert",
                    "无法解析当前 Cookie 的到期时间，已跳过即将到期提醒监测。");
            }

            return;
        }

        _observedExpirationTime = expirationTime;
        activityLogService.Write(
            LogEntryKind.Info,
            "Alert",
            $"已开始监测 Cookie 到期时间：{expirationTime:yyyy-MM-dd HH:mm:ss zzz}。");
    }

    private void ClearCurrentObservation()
    {
        _observedCookieFingerprint = null;
        _observedExpirationTime = null;
        _nextDispatchAttemptAt = null;
    }

    private void ScheduleNextDispatchAttempt(DateTimeOffset now, TimeSpan remaining)
    {
        var retryInterval = remaining <= DisabledOrFailedRetryInterval
            ? NearExpirationRetryInterval
            : DisabledOrFailedRetryInterval;
        _nextDispatchAttemptAt = now + retryInterval;
    }

    private static string ComputeFingerprint(string cookie)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cookie));
        return Convert.ToHexString(hash);
    }
}
