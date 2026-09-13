using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

internal sealed class NetworkLogFaultReporter(IAppLogWriter writer, TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastReport;
    private int _suppressed;

    public void Report(Exception exception)
    {
        int suppressed;
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            if (_lastReport is { } last && now - last < TimeSpan.FromMinutes(1))
            {
                _suppressed++;
                return;
            }
            _lastReport = now;
            suppressed = _suppressed;
            _suppressed = 0;
        }
        try
        {
            writer.Write(LogLevel.Warning, "Logging",
                $"网络日志采集失败，业务请求继续执行。异常类型={exception.GetType().Name}；本次合并数量={suppressed}。",
                eventId: new EventId(1101, "NetworkLogCaptureFailed"));
        }
        catch { /* A broken logger must not recursively report its own failure. */ }
    }
}
