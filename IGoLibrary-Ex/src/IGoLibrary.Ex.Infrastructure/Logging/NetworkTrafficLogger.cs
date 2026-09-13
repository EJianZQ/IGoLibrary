using System.Diagnostics;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class NetworkTrafficLogger(NetworkLogState state, IAppLogWriter writer, TimeProvider? timeProvider = null)
{
    private readonly NetworkLogFaultReporter _faults = new(writer, timeProvider ?? TimeProvider.System);
    public const string Category = "NetworkTraffic";
    public NetworkLogSession? Start(string service, string direction, string method, string? address)
    {
        var generation = state.CaptureGeneration();
        if (generation == 0) return null;
        try
        {
            var session = new NetworkLogSession(state, generation, writer, _faults);
            session.Write($"开始；服务={service}；方向={direction}；方法={method}；地址={NetworkLogSanitizer.Address(address, service)}；任务={Activity.Current?.TraceId.ToString() ?? "无"}");
            return session;
        }
        catch (Exception ex) { _faults.Report(ex); return null; }
    }
}

public sealed class NetworkLogSession
{
    private readonly NetworkLogState _state;
    private readonly long _generation;
    private readonly IAppLogWriter _writer;
    private readonly NetworkLogFaultReporter _faults;
    internal NetworkLogSession(NetworkLogState state, long generation, IAppLogWriter writer, NetworkLogFaultReporter faults)
    {
        _state = state;
        _generation = generation;
        _writer = writer;
        _faults = faults;
    }
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly long _startedAt = Stopwatch.GetTimestamp();
    public bool IsActive => _state.IsCurrent(_generation);
    public void CaptureFailure(Exception exception) => _faults.Report(exception);
    public NetworkBodyCapture CreateBody(Func<bool>? captureBytes = null) => new(() => IsActive, captureBytes);

    public void Write(string message, LogLevel level = LogLevel.Information)
    {
        if (!IsActive) return;
        try
        {
            _writer.Write(level, NetworkTrafficLogger.Category,
                $"网络交互={_id}；耗时毫秒={Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds:0.###}；{NetworkLogSanitizer.Text(message)}",
                eventId: new EventId(1100, "NetworkTraffic"));
        }
        catch (Exception ex) { _faults.Report(ex); }
    }

    // Transport tokens include timeout and shutdown tokens as well as user cancellation.
    // Only the caller that creates those tokens can distinguish their source reliably.
    public void Failure(Exception exception) => Write(
        $"异常结束；结果={(exception is OperationCanceledException ? "超时或取消" : exception is TimeoutException ? "超时" : "传输失败")}；异常类型={exception.GetType().Name}",
        exception is OperationCanceledException ? LogLevel.Warning : LogLevel.Error);
}
