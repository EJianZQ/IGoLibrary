using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class NetworkRequestSecurityAuditor(
    ILogger logger,
    string serviceName,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan RejectedRequestLogInterval = TimeSpan.FromSeconds(30);
    private readonly object _gate = new();
    private readonly byte[] _sourceHashSalt = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _lastRejectedRequestLoggedAt = DateTimeOffset.MinValue;
    private int _suppressedRejectedRequestCount;

    public bool IsValidToken(HttpContext context, string expectedToken)
    {
        var valid = string.Equals(
            context.Request.Query["token"].ToString(),
            expectedToken,
            StringComparison.Ordinal);
        if (!valid)
        {
            LogRejectedRequest(context);
        }

        return valid;
    }

    private void LogRejectedRequest(HttpContext context)
    {
        var now = _timeProvider.GetUtcNow();
        int suppressedCount;
        lock (_gate)
        {
            if (now - _lastRejectedRequestLoggedAt < RejectedRequestLogInterval)
            {
                _suppressedRejectedRequestCount++;
                return;
            }

            suppressedCount = _suppressedRejectedRequestCount;
            _suppressedRejectedRequestCount = 0;
            _lastRejectedRequestLoggedAt = now;
        }

        logger.LogWarning(
            "{ServiceName}拒绝未授权请求。方法={Method}，路径={Path}，来源哈希={SourceHash}，本次合并数量={SuppressedCount}。",
            serviceName,
            context.Request.Method,
            context.Request.Path.Value,
            GetSourceHash(context),
            suppressedCount);
    }

    private string GetSourceHash(HttpContext context)
    {
        var source = context.Request.Headers["CF-Connecting-IP"].ToString();
        if (string.IsNullOrWhiteSpace(source))
        {
            source = context.Connection.RemoteIpAddress?.ToString();
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = context.Connection.Id;
        }

        var hash = HMACSHA256.HashData(
            _sourceHashSalt,
            Encoding.UTF8.GetBytes(source ?? "unknown"));
        return Convert.ToHexString(hash.AsSpan(0, 6));
    }
}
