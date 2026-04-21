using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppLogFileWriter : IAppLogWriter, IDisposable
{
    private const int DefaultRetainedFileCount = 14;
    private const int DefaultQueueCapacity = 2048;
    private const int FlushBatchSize = 20;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly string _logDirectory;
    private readonly int _retainedFileCount;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Channel<QueuedWorkItem> _queue;
    private readonly Task _processingTask;
    private readonly object _disposeGate = new();
    private readonly TimeSpan _simulatedWriteDelay;
    private long _droppedEntryCount;
    private bool _disposed;

    public AppLogFileWriter()
        : this(logDirectory: null)
    {
    }

    public AppLogFileWriter(
        string? logDirectory,
        int retainedFileCount = DefaultRetainedFileCount,
        Func<DateTimeOffset>? clock = null)
        : this(logDirectory, retainedFileCount, clock, DefaultQueueCapacity, TimeSpan.Zero)
    {
    }

    internal AppLogFileWriter(
        string? logDirectory,
        int retainedFileCount,
        Func<DateTimeOffset>? clock,
        int queueCapacity,
        TimeSpan simulatedWriteDelay)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? AppDataPaths.LogsDirectory
            : Path.GetFullPath(logDirectory);
        _retainedFileCount = Math.Max(1, retainedFileCount);
        _clock = clock ?? (() => DateTimeOffset.Now);
        _simulatedWriteDelay = simulatedWriteDelay > TimeSpan.Zero ? simulatedWriteDelay : TimeSpan.Zero;
        _queue = Channel.CreateBounded<QueuedWorkItem>(new BoundedChannelOptions(Math.Max(16, queueCapacity))
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _processingTask = Task.Run(ProcessQueueAsync);
    }

    public void Write(
        LogLevel level,
        string category,
        string message,
        Exception? exception = null,
        EventId eventId = default,
        DateTimeOffset? timestamp = null)
    {
        if (level == LogLevel.None)
        {
            return;
        }

        var effectiveTimestamp = timestamp ?? _clock();
        var line = BuildLine(effectiveTimestamp, level, category, message, exception, eventId);
        var requiresFlush = level >= LogLevel.Error || exception is not null;

        lock (_disposeGate)
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

    public void Flush()
    {
        TaskCompletionSource flushCompletion;
        lock (_disposeGate)
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
        lock (_disposeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.Writer.TryComplete();
        }

        try
        {
            _processingTask.GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task ProcessQueueAsync()
    {
        StreamWriter? activeWriter = null;
        string? activeFilePath = null;
        DateOnly activeDate = default;
        DateOnly lastCleanupDate = default;
        var pendingWriteCount = 0;
        var flushStopwatch = Stopwatch.StartNew();

        try
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync())
            {
                switch (workItem)
                {
                    case QueuedLogEntry entry:
                        try
                        {
                            activeWriter = await EnsureWriterAsync(
                                entry.Timestamp,
                                activeWriter,
                                activeFilePath,
                                activeDate,
                                lastCleanupDate);
                            activeFilePath = BuildLogFilePath(entry.Timestamp);
                            activeDate = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime.Date);
                            lastCleanupDate = activeDate;

                            pendingWriteCount += await WriteDroppedEntryWarningAsync(activeWriter, entry.Timestamp);
                            if (_simulatedWriteDelay > TimeSpan.Zero)
                            {
                                await Task.Delay(_simulatedWriteDelay);
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
                        catch
                        {
                            if (activeWriter is not null)
                            {
                                await activeWriter.DisposeAsync();
                                activeWriter = null;
                                activeFilePath = null;
                                activeDate = default;
                            }

                            pendingWriteCount = 0;
                            flushStopwatch.Restart();
                        }

                        break;
                    case FlushRequest flushRequest:
                        try
                        {
                            if (activeWriter is null && Interlocked.Read(ref _droppedEntryCount) <= 0)
                            {
                                flushRequest.Completion.SetResult();
                                break;
                            }

                            if (activeWriter is null)
                            {
                                var flushTimestamp = _clock();
                                activeWriter = await EnsureWriterAsync(
                                    flushTimestamp,
                                    activeWriter,
                                    activeFilePath,
                                    activeDate,
                                    lastCleanupDate);
                                activeFilePath = BuildLogFilePath(flushTimestamp);
                                activeDate = DateOnly.FromDateTime(flushTimestamp.LocalDateTime.Date);
                                lastCleanupDate = activeDate;
                            }

                            pendingWriteCount += await WriteDroppedEntryWarningAsync(activeWriter, _clock());
                            if (pendingWriteCount > 0)
                            {
                                await activeWriter.FlushAsync();
                                pendingWriteCount = 0;
                                flushStopwatch.Restart();
                            }

                            flushRequest.Completion.SetResult();
                        }
                        catch (Exception ex)
                        {
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
                if (pendingWriteCount > 0)
                {
                    await activeWriter.FlushAsync();
                }

                await activeWriter.DisposeAsync();
            }
        }
    }

    private async Task<StreamWriter> EnsureWriterAsync(
        DateTimeOffset timestamp,
        StreamWriter? activeWriter,
        string? activeFilePath,
        DateOnly activeDate,
        DateOnly lastCleanupDate)
    {
        Directory.CreateDirectory(_logDirectory);

        var currentDate = DateOnly.FromDateTime(timestamp.LocalDateTime.Date);
        var filePath = BuildLogFilePath(timestamp);
        if (activeWriter is not null &&
            activeFilePath is not null &&
            activeDate == currentDate &&
            string.Equals(activeFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return activeWriter;
        }

        if (activeWriter is not null)
        {
            await activeWriter.DisposeAsync();
        }

        var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        var writer = new StreamWriter(stream, Encoding.UTF8);

        if (lastCleanupDate != currentDate)
        {
            CleanupOldFiles();
        }

        return writer;
    }

    private string BuildLogFilePath(DateTimeOffset timestamp)
    {
        return Path.Combine(_logDirectory, $"app-{timestamp:yyyyMMdd}.log");
    }

    private void CleanupOldFiles()
    {
        try
        {
            var files = new DirectoryInfo(_logDirectory)
                .GetFiles("app-*.log", SearchOption.TopDirectoryOnly)
                .Select(file =>
                {
                    var hasParsedDate = TryParseLogDate(file.Name, out var parsedDate);
                    return new
                    {
                        File = file,
                        ParsedDate = hasParsedDate ? parsedDate : DateOnly.MinValue,
                        HasParsedDate = hasParsedDate
                    };
                })
                .OrderByDescending(item => item.HasParsedDate)
                .ThenByDescending(item => item.ParsedDate)
                .ThenByDescending(item => item.File.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files.Skip(_retainedFileCount))
            {
                file.File.Delete();
            }
        }
        catch
        {
        }
    }

    private static bool TryParseLogDate(string fileName, out DateOnly date)
    {
        const string prefix = "app-";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (!fileNameWithoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            date = default;
            return false;
        }

        return DateOnly.TryParseExact(
            fileNameWithoutExtension[prefix.Length..],
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
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
        builder.Append(NormalizeCategory(category));

        if (eventId != default && (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name)))
        {
            builder.Append(" (EventId=");
            builder.Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(':');
                builder.Append(eventId.Name);
            }

            builder.Append(')');
        }

        builder.Append(" - ");
        builder.AppendLine(NormalizeMessage(message));

        if (exception is not null)
        {
            foreach (var line in exception.ToString().ReplaceLineEndings("\n").Split('\n'))
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

        var line = BuildLine(
            timestamp,
            LogLevel.Warning,
            "Logging",
            $"日志队列已满，已丢弃 {droppedCount} 条日志。",
            exception: null,
            eventId: default);
        await writer.WriteAsync(line);
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

    private abstract record QueuedWorkItem;

    private sealed record QueuedLogEntry(DateTimeOffset Timestamp, string Line, bool RequiresFlush) : QueuedWorkItem;

    private sealed record FlushRequest(TaskCompletionSource Completion) : QueuedWorkItem;
}
