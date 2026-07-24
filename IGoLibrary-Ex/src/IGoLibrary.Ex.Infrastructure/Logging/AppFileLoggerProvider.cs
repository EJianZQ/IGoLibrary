using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppFileLoggerProvider(IAppLogWriter logWriter) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName)
    {
        return new AppFileLogger(categoryName, logWriter, () => _scopeProvider);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    private sealed class AppFileLogger(
        string categoryName,
        IAppLogWriter logWriter,
        Func<IExternalScopeProvider> getScopeProvider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return getScopeProvider().Push(state);
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

            var scopes = new List<string>();
            getScopeProvider().ForEachScope(
                static (scope, values) =>
                {
                    var rendered = scope?.ToString();
                    if (!string.IsNullOrWhiteSpace(rendered))
                    {
                        values.Add(rendered);
                    }
                },
                scopes);
            if (scopes.Count > 0)
            {
                message = $"{message} | 作用域={string.Join(" => ", scopes)}";
            }

            logWriter.Write(logLevel, categoryName, message, exception, eventId);
        }
    }
}
