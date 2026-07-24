using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppLogFileWriter : IAppLogWriter, IAppLogRuntimeController, IDisposable
{
    private const int DefaultQueueCapacity = 2048;
    private const int FlushBatchSize = 20;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan HealthFailureNotificationInterval = TimeSpan.FromMinutes(1);

    private readonly string _logDirectory;
    private readonly DateTimeOffset _runStartedAt;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Channel<QueuedWorkItem> _queue;
    private readonly TaskCompletionSource<InitialConfiguration> _initialConfiguration =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _processingTask;
    private readonly object _stateGate = new();
    private readonly Func<Task>? _beforeWriteAsync;
    private long _droppedEntryCount;
    private int _acceptingWrites = 1;
    private int _consecutiveFailureCount;
    private DateTimeOffset _lastHealthFailureNotificationAt = DateTimeOffset.MinValue;
    private bool _configured;
    private bool _disposed;

    public event EventHandler<AppLogWriterHealthChangedEventArgs>? HealthChanged;

    public bool IsHealthy => Volatile.Read(ref _consecutiveFailureCount) == 0;

    public int ConsecutiveFailureCount => Volatile.Read(ref _consecutiveFailureCount);

    public AppLogFileWriter()
        : this(logDirectory: null)
    {
    }

    public AppLogFileWriter(StorageLocations locations, bool startUnconfigured = false)
        : this(
            locations.LogDirectory,
            LogFileSettings.DefaultRetainedFileCount,
            clock: null,
            DefaultQueueCapacity,
            beforeWriteAsync: null,
            startUnconfigured)
    {
    }

    public AppLogFileWriter(
        string? logDirectory,
        int retainedFileCount = LogFileSettings.DefaultRetainedFileCount,
        Func<DateTimeOffset>? clock = null)
        : this(
            logDirectory,
            retainedFileCount,
            clock,
            DefaultQueueCapacity,
            beforeWriteAsync: null,
            startUnconfigured: false)
    {
    }

    internal AppLogFileWriter(
        string? logDirectory,
        int retainedFileCount,
        Func<DateTimeOffset>? clock,
        int queueCapacity,
        Func<Task>? beforeWriteAsync,
        bool startUnconfigured = false)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? StorageLocationDefaults.GetDefaults().LogDirectory
            : Path.GetFullPath(logDirectory);
        _clock = clock ?? (() => DateTimeOffset.Now);
        _runStartedAt = _clock();
        _beforeWriteAsync = beforeWriteAsync;
        _queue = Channel.CreateBounded<QueuedWorkItem>(new BoundedChannelOptions(Math.Max(16, queueCapacity))
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _processingTask = Task.Run(ProcessQueueAsync);

        if (!startUnconfigured)
        {
            ConfigureInitial(new LogFileSettings(true, retainedFileCount));
        }
    }

    public void Write(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        EventId eventId = default,
        DateTimeOffset? timestamp = null)
    {
        if (level == LogLevel.None || Volatile.Read(ref _acceptingWrites) == 0)
        {
            return;
        }

        var effectiveTimestamp = timestamp ?? _clock();
        var line = BuildLine(effectiveTimestamp, level, category, message, exception, eventId);
        var requiresFlush = level >= LogLevel.Error || exception is not null;

        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (!_queue.Writer.TryWrite(new QueuedLogEntry(effectiveTimestamp, line, requiresFlush)))
        {
            Interlocked.Increment(ref _droppedEntryCount);
        }
    }

    public async Task<LogRuntimeApplyResult> ApplyAsync(
        LogFileSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = LogFileSettings.Normalize(settings);
        Task<LogRuntimeApplyResult> completionTask;
        ApplySettingsRequest? request = null;
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_configured)
            {
                completionTask = ConfigureInitial(normalized);
            }
            else
            {
                if (!normalized.Enabled)
                {
                    Volatile.Write(ref _acceptingWrites, 0);
                }

                var completion = new TaskCompletionSource<LogRuntimeApplyResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                completionTask = completion.Task;
                request = new ApplySettingsRequest(normalized, completion);
            }
        }

        if (request is not null)
        {
            await _queue.Writer.WriteAsync(request, CancellationToken.None);
        }

        var result = await completionTask;
        if (normalized.Enabled)
        {
            Volatile.Write(ref _acceptingWrites, 1);
        }

        return result;
    }

    public void Flush()
    {
        EnsureConfiguredForShutdown();
        TaskCompletionSource flushCompletion;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            flushCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            _queue.Writer.WriteAsync(new FlushRequest(flushCompletion)).AsTask().GetAwaiter().GetResult();
            flushCompletion.Task.GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
        }
    }

    public void Dispose()
    {
        EnsureConfiguredForShutdown();
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Volatile.Write(ref _acceptingWrites, 0);
            _queue.Writer.TryComplete();
        }

        try
        {
            _processingTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ReportFailure("释放日志写入队列", ex);
        }
    }

    private Task<LogRuntimeApplyResult> ConfigureInitial(LogFileSettings settings)
    {
        var normalized = LogFileSettings.Normalize(settings);
        var completion = new TaskCompletionSource<LogRuntimeApplyResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _configured = true;
        if (!normalized.Enabled)
        {
            Volatile.Write(ref _acceptingWrites, 0);
        }

        _initialConfiguration.TrySetResult(new InitialConfiguration(normalized, completion));
        return completion.Task;
    }

    private void EnsureConfiguredForShutdown()
    {
        lock (_stateGate)
        {
            if (!_configured && !_disposed)
            {
                ConfigureInitial(LogFileSettings.Default);
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        StreamWriter? activeWriter = null;
        string? activeFilePath = null;
        var pendingWriteCount = 0;
        var flushStopwatch = Stopwatch.StartNew();
        var initial = await _initialConfiguration.Task;
        var currentSettings = initial.Settings;

        try
        {
            initial.Completion.SetResult(ApplyFilePolicies(
                currentSettings,
                activeFilePath,
                deleteLegacyFiles: true,
                enforceRetention: true));
            await foreach (var workItem in _queue.Reader.ReadAllAsync())
            {
                switch (workItem)
                {
                    case QueuedLogEntry entry when currentSettings.Enabled:
                        try
                        {
                            if (activeWriter is null)
                            {
                                (activeFilePath, activeWriter) = CreateWriter(activeFilePath);
                                var creationCleanup = AppLogFileCatalog.EnforceRetention(
                                    _logDirectory,
                                    currentSettings.RetainedFileCount,
                                    activeFilePath,
                                    ReportCatalogFailure);
                                _ = creationCleanup;
                            }

                            pendingWriteCount += await WriteDroppedEntryWarningAsync(activeWriter, entry.Timestamp);
                            if (_beforeWriteAsync is not null)
                            {
                                await _beforeWriteAsync();
                            }

                            await activeWriter.WriteAsync(entry.Line);
                            pendingWriteCount++;
                            if (entry.RequiresFlush ||
                                pendingWriteCount >= FlushBatchSize ||
                                flushStopwatch.Elapsed >= FlushInterval)
                            {
                                await activeWriter.FlushAsync();
                                pendingWriteCount = 0;
                                flushStopwatch.Restart();
                            }
                        }
                        catch (Exception ex)
                        {
                            ReportFailure("写入日志文件", ex);
                            if (activeWriter is not null)
                            {
                                try
                                {
                                    await activeWriter.DisposeAsync();
                                }
                                catch (Exception disposeException)
                                {
                                    ReportFailure("释放失效的日志文件", disposeException);
                                }

                                activeWriter = null;
                                activeFilePath = null;
                            }

                            pendingWriteCount = 0;
                            flushStopwatch.Restart();
                        }
                        finally
                        {
                            if (activeWriter is not null)
                            {
                                ReportRecovered("写入日志文件");
                            }
                        }

                        break;

                    case ApplySettingsRequest request:
                        try
                        {
                            if (activeWriter is not null &&
                                (!request.Settings.Enabled ||
                                 request.Settings.RetainedFileCount != currentSettings.RetainedFileCount))
                            {
                                await FlushWriterAsync(activeWriter, pendingWriteCount);
                                pendingWriteCount = 0;
                            }

                            if (!request.Settings.Enabled && activeWriter is not null)
                            {
                                await activeWriter.DisposeAsync();
                                activeWriter = null;
                            }

                            var retainedFileCountChanged =
                                request.Settings.RetainedFileCount != currentSettings.RetainedFileCount;
                            currentSettings = request.Settings;
                            request.Completion.SetResult(ApplyFilePolicies(
                                currentSettings,
                                activeFilePath,
                                deleteLegacyFiles: true,
                                enforceRetention: retainedFileCountChanged));
                            flushStopwatch.Restart();
                        }
                        catch (Exception ex)
                        {
                            ReportFailure("应用日志设置", ex);
                            request.Completion.SetException(ex);
                        }

                        break;

                    case FlushRequest flushRequest:
                        try
                        {
                            if (activeWriter is not null)
                            {
                                pendingWriteCount += await WriteDroppedEntryWarningAsync(activeWriter, _clock());
                                await FlushWriterAsync(activeWriter, pendingWriteCount);
                                pendingWriteCount = 0;
                                flushStopwatch.Restart();
                            }

                            flushRequest.Completion.SetResult();
                        }
                        catch (Exception ex)
                        {
                            ReportFailure("刷新日志文件", ex);
                            flushRequest.Completion.SetException(ex);
                        }

                        break;
                }
            }
        }
        finally
        {
            if (activeWriter is not null)
            {
                pendingWriteCount += await WriteDroppedEntryWarningAsync(activeWriter, _clock());
                await FlushWriterAsync(activeWriter, pendingWriteCount);
                await activeWriter.DisposeAsync();
            }
        }
    }

    private (string Path, StreamWriter Writer) CreateWriter(string? existingPath)
    {
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            var existingStream = new FileStream(
                existingPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            return (existingPath, new StreamWriter(existingStream, Encoding.UTF8));
        }

        var (path, stream) = AppLogFileCatalog.CreateRunFile(_logDirectory, _runStartedAt);
        return (path, new StreamWriter(stream, Encoding.UTF8));
    }

    private LogRuntimeApplyResult ApplyFilePolicies(
        LogFileSettings settings,
        string? activeFilePath,
        bool deleteLegacyFiles,
        bool enforceRetention)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            ReportFailure("创建日志目录", ex);
            return new LogRuntimeApplyResult(1, 1);
        }
        var legacyFailures = deleteLegacyFiles
            ? AppLogFileCatalog.DeleteLegacyDailyFiles(_logDirectory, ReportCatalogFailure)
            : 0;
        var retentionFailures = enforceRetention
            ? AppLogFileCatalog.EnforceRetention(
                _logDirectory,
                settings.RetainedFileCount,
                activeFilePath,
                ReportCatalogFailure)
            : 0;
        return new LogRuntimeApplyResult(legacyFailures, retentionFailures);
    }

    private static Task FlushWriterAsync(StreamWriter writer, int pendingWriteCount)
    {
        return pendingWriteCount > 0 ? writer.FlushAsync() : Task.CompletedTask;
    }

    private static string BuildLine(
        DateTimeOffset timestamp,
        LogLevel level,
        string category,
        string message,
        Exception? exception,
        EventId eventId)
    {
        var builder = new StringBuilder();
        builder.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        builder.Append(' ');
        builder.Append('[');
        builder.Append(GetLevelCode(level));
        builder.Append("] ");
        builder.Append(NormalizeCategory(AppLogSanitizer.Sanitize(category)));

        if (eventId != default && (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name)))
        {
            builder.Append(" (事件编号=");
            builder.Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(':');
                builder.Append(AppLogSanitizer.Sanitize(eventId.Name));
            }

            builder.Append(')');
        }

        builder.Append(" - ");
        builder.AppendLine(NormalizeMessage(AppLogSanitizer.Sanitize(message)));

        if (exception is not null)
        {
            var sanitizedException = AppLogSanitizer.Sanitize(exception.ToString());
            foreach (var line in sanitizedException.ReplaceLineEndings("\n").Split('\n'))
            {
                builder.Append("    ");
                builder.AppendLine(line);
            }
        }

        return builder.ToString();
    }

    private async Task<int> WriteDroppedEntryWarningAsync(StreamWriter writer, DateTimeOffset timestamp)
    {
        var droppedCount = Interlocked.Exchange(ref _droppedEntryCount, 0);
        if (droppedCount <= 0)
        {
            return 0;
        }

        await writer.WriteAsync(BuildLine(
            timestamp,
            LogLevel.Warning,
            "Logging",
            $"日志队列已满，已丢弃 {droppedCount} 条日志。",
            exception: null,
            eventId: default));
        return 1;
    }

    private static string NormalizeCategory(string category)
    {
        return string.IsNullOrWhiteSpace(category)
            ? "App"
            : category.ReplaceLineEndings(" ").Trim();
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "(empty message)";
        }

        return message.ReplaceLineEndings("\\n").Trim();
    }

    private static string GetLevelCode(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "UNK"
        };
    }

    private void ReportFailure(string operation, Exception exception)
    {
        var failureCount = Interlocked.Increment(ref _consecutiveFailureCount);
        var now = _clock();
        bool shouldNotify;
        lock (_stateGate)
        {
            shouldNotify = failureCount == 1 ||
                           now - _lastHealthFailureNotificationAt >= HealthFailureNotificationInterval;
            if (shouldNotify)
            {
                _lastHealthFailureNotificationAt = now;
            }
        }

        if (!shouldNotify)
        {
            return;
        }

        PublishHealthChanged(new AppLogWriterHealthChangedEventArgs(
            isHealthy: false,
            failureCount,
            now,
            operation,
            AppLogSanitizer.Sanitize($"{exception.GetType().Name}: {exception.Message}")));
    }

    private void ReportCatalogFailure(string operation, Exception exception)
    {
        ReportFailure(operation, exception);
        Write(
            LogLevel.Warning,
            "Logging",
            $"{operation}失败。",
            exception,
            new EventId(1002, "LogCatalogOperationFailed"));
    }

    private void ReportRecovered(string operation)
    {
        var previousFailureCount = Interlocked.Exchange(ref _consecutiveFailureCount, 0);
        if (previousFailureCount == 0)
        {
            return;
        }

        PublishHealthChanged(new AppLogWriterHealthChangedEventArgs(
            isHealthy: true,
            consecutiveFailureCount: 0,
            _clock(),
            operation));
    }

    private void PublishHealthChanged(AppLogWriterHealthChangedEventArgs args)
    {
        var handlers = HealthChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<AppLogWriterHealthChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
            }
        }
    }

    private abstract record QueuedWorkItem;

    private sealed record QueuedLogEntry(
        DateTimeOffset Timestamp,
        string Line,
        bool RequiresFlush) : QueuedWorkItem;

    private sealed record ApplySettingsRequest(
        LogFileSettings Settings,
        TaskCompletionSource<LogRuntimeApplyResult> Completion) : QueuedWorkItem;

    private sealed record FlushRequest(TaskCompletionSource Completion) : QueuedWorkItem;

    private sealed record InitialConfiguration(
        LogFileSettings Settings,
        TaskCompletionSource<LogRuntimeApplyResult> Completion);
}
