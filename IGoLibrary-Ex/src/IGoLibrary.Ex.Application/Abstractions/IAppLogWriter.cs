using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IAppLogWriter
{
    void Write(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        EventId eventId = default,
        DateTimeOffset? timestamp = null);

    void Flush();
}
