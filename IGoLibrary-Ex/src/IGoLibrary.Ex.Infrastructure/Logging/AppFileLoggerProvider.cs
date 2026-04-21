using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppFileLoggerProvider(IAppLogWriter logWriter) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new AppFileLogger(categoryName, logWriter);
    }

    public void Dispose()
    {
    }

    private sealed class AppFileLogger(string categoryName, IAppLogWriter logWriter) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = exception?.Message ?? "(empty message)";
            }

            logWriter.Write(logLevel, categoryName, message, exception, eventId);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
