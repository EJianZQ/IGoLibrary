using System.Diagnostics;
using System.Text;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppTraceListener(IAppLogWriter logWriter) : TraceListener
{
    private readonly object _gate = new();
    private readonly ThreadLocal<StringBuilder> _threadBuffer = new(() => new StringBuilder(), trackAllValues: true);

    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var buffer = _threadBuffer.Value;
        if (buffer is null)
        {
            return;
        }

        lock (_gate)
        {
            buffer.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        var buffer = _threadBuffer.Value;
        if (buffer is null)
        {
            return;
        }

        string? line;
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(message))
            {
                buffer.Append(message);
            }

            line = DrainBuffer(buffer);
        }

        if (line is not null)
        {
            logWriter.Write(LogLevel.Debug, "Trace", line);
        }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string? source, TraceEventType eventType, int id, string? message)
    {
        FlushCurrentThreadBuffer();
        logWriter.Write(
            MapLogLevel(eventType),
            string.IsNullOrWhiteSpace(source) ? "Trace" : source,
            string.IsNullOrWhiteSpace(message) ? $"Trace event: {eventType}" : message!,
            eventId: new EventId(id, eventType.ToString()));
    }

    public override void Flush()
    {
        FlushAllBuffers();
        logWriter.Flush();
        base.Flush();
    }

    public override void Fail(string? message, string? detailMessage)
    {
        FlushCurrentThreadBuffer();
        var renderedMessage = string.IsNullOrWhiteSpace(detailMessage)
            ? message ?? "Trace failure"
            : $"{message} {detailMessage}".Trim();
        logWriter.Write(LogLevel.Error, "Trace", renderedMessage);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Flush();
            _threadBuffer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void FlushCurrentThreadBuffer()
    {
        var buffer = _threadBuffer.Value;
        if (buffer is null)
        {
            return;
        }

        FlushBuffer(buffer);
    }

    private void FlushAllBuffers()
    {
        foreach (var buffer in _threadBuffer.Values)
        {
            FlushBuffer(buffer);
        }
    }

    private void FlushBuffer(StringBuilder buffer)
    {
        string? line;
        lock (_gate)
        {
            line = DrainBuffer(buffer);
        }

        if (line is not null)
        {
            logWriter.Write(LogLevel.Debug, "Trace", line);
        }
    }

    private static string? DrainBuffer(StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return null;
        }

        var line = buffer.ToString().TrimEnd();
        buffer.Clear();
        return line.Length == 0 ? null : line;
    }

    private static LogLevel MapLogLevel(TraceEventType eventType)
    {
        return eventType switch
        {
            TraceEventType.Critical => LogLevel.Critical,
            TraceEventType.Error => LogLevel.Error,
            TraceEventType.Warning => LogLevel.Warning,
            TraceEventType.Information => LogLevel.Information,
            TraceEventType.Verbose => LogLevel.Debug,
            TraceEventType.Start => LogLevel.Information,
            TraceEventType.Stop => LogLevel.Information,
            TraceEventType.Suspend => LogLevel.Warning,
            TraceEventType.Resume => LogLevel.Information,
            TraceEventType.Transfer => LogLevel.Debug,
            _ => LogLevel.Debug
        };
    }
}
