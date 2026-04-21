using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-LoggingTests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ActivityLogService_Write_PreservesUiEntries_And_ForwardsToSharedWriter()
    {
        var writer = new CollectingLogWriter();
        var service = new ActivityLogService(writer);

        service.Write(LogEntryKind.Warning, "Grab", "第 1 次轮询未命中。");

        var entry = Assert.Single(service.Entries);
        Assert.Equal(LogEntryKind.Warning, entry.Kind);
        Assert.Equal("Grab", entry.Category);
        Assert.Equal("第 1 次轮询未命中。", entry.Message);

        var written = Assert.Single(writer.Entries);
        Assert.Equal(LogLevel.Warning, written.Level);
        Assert.Equal("Activity.Grab", written.Category);
        Assert.Equal("第 1 次轮询未命中。", written.Message);
    }

    [Fact]
    public void ActivityLogService_Write_KeepsOnlyLatest500Entries()
    {
        var service = new ActivityLogService();

        for (var index = 0; index < 510; index++)
        {
            service.Write(LogEntryKind.Info, "Grab", $"日志 {index}");
        }

        Assert.Equal(500, service.Entries.Count);
        Assert.Equal("日志 10", service.Entries[0].Message);
        Assert.Equal("日志 509", service.Entries[^1].Message);
    }

    [Fact]
    public void AppLogFileWriter_Write_CreatesDailyLogFile_WithStructuredLine()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 9, 30, 15, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(
            LogLevel.Error,
            "Activity.Occupy",
            "重新预约失败。",
            new InvalidOperationException("接口返回失败"));

        writer.Dispose();

        var logFile = Path.Combine(_tempDirectory, "app-20260420.log");
        Assert.True(File.Exists(logFile));

        var content = File.ReadAllText(logFile);
        Assert.Contains("2026-04-20 09:30:15.000 +08:00 [ERR] Activity.Occupy - 重新预约失败。", content);
        Assert.Contains("InvalidOperationException: 接口返回失败", content);
    }

    [Fact]
    public void AppLogFileWriter_Write_EscapesMultiLineMessages_IntoSingleStructuredLine()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(LogLevel.Warning, "Activity.Auth", "第一行\r\n第二行\n第三行");
        writer.Flush();
        writer.Dispose();

        var logFile = Path.Combine(_tempDirectory, "app-20260420.log");
        var lines = File.ReadAllLines(logFile);

        var line = Assert.Single(lines);
        Assert.Contains("第一行\\n第二行\\n第三行", line);
    }

    [Fact]
    public void AppLogFileWriter_CleanupOldFiles_UsesFileDateInsteadOfCreationOrder()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260420.log"), "newest");
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260418.log"), "oldest");
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260419.log"), "middle");

        using var writer = new AppLogFileWriter(
            _tempDirectory,
            retainedFileCount: 2,
            clock: () => new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.FromHours(8)));

        writer.Write(LogLevel.Information, "Global", "cleanup-trigger");
        writer.Dispose();

        Assert.True(File.Exists(Path.Combine(_tempDirectory, "app-20260421.log")));
        Assert.True(File.Exists(Path.Combine(_tempDirectory, "app-20260420.log")));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "app-20260419.log")));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "app-20260418.log")));
    }

    [Fact]
    public void AppLogFileWriter_WhenQueueIsBounded_RecordsDroppedEntryWarning()
    {
        var timestamp = new DateTimeOffset(2026, 4, 21, 8, 0, 0, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(
            _tempDirectory,
            retainedFileCount: 14,
            clock: () => timestamp,
            queueCapacity: 16,
            simulatedWriteDelay: TimeSpan.FromMilliseconds(25));

        for (var index = 0; index < 256; index++)
        {
            writer.Write(LogLevel.Information, "Grab", $"日志 {index}");
        }

        writer.Flush();
        writer.Dispose();

        var logFile = Path.Combine(_tempDirectory, "app-20260421.log");
        var content = File.ReadAllText(logFile);

        Assert.Contains("日志队列已满，已丢弃", content);
    }

    [Fact]
    public void AppTraceListener_WriteLine_KeepsPerThreadBuffersSeparated()
    {
        var writer = new CollectingLogWriter();
        using var listener = new AppTraceListener(writer);
        using var partialWritesCompleted = new CountdownEvent(2);
        using var releaseFlush = new ManualResetEventSlim(false);

        var threadA = new Thread(() =>
        {
            listener.Write("Grab-");
            partialWritesCompleted.Signal();
            releaseFlush.Wait();
            listener.WriteLine("1");
        });

        var threadB = new Thread(() =>
        {
            listener.Write("Occupy-");
            partialWritesCompleted.Signal();
            releaseFlush.Wait();
            listener.WriteLine("2");
        });

        threadA.Start();
        threadB.Start();
        Assert.True(partialWritesCompleted.Wait(TimeSpan.FromSeconds(5)));
        releaseFlush.Set();
        Assert.True(threadA.Join(TimeSpan.FromSeconds(5)));
        Assert.True(threadB.Join(TimeSpan.FromSeconds(5)));

        Assert.Contains(writer.Entries, entry => entry.Category == "Trace" && entry.Message == "Grab-1");
        Assert.Contains(writer.Entries, entry => entry.Category == "Trace" && entry.Message == "Occupy-2");
    }

    [Fact]
    public void AppTraceListener_Flush_PersistsBufferedPartialWrite()
    {
        var writer = new CollectingLogWriter();
        using var listener = new AppTraceListener(writer);

        listener.Write("Partial trace");
        listener.Flush();

        var entry = Assert.Single(writer.Entries);
        Assert.Equal("Trace", entry.Category);
        Assert.Equal("Partial trace", entry.Message);
        Assert.Equal(1, writer.FlushCalls);
    }

    [Fact]
    public void AppTraceListener_Dispose_FlushesBufferedWritesFromOtherThreads()
    {
        var writer = new CollectingLogWriter();
        var listener = new AppTraceListener(writer);

        var thread = new Thread(() => listener.Write("Background partial trace"));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

        listener.Dispose();

        Assert.Contains(writer.Entries, entry => entry.Category == "Trace" && entry.Message == "Background partial trace");
    }

    private sealed class CollectingLogWriter : IAppLogWriter
    {
        private readonly object _gate = new();

        public List<(LogLevel Level, string Category, string Message)> Entries { get; } = [];

        public int FlushCalls { get; private set; }

        public void Write(
            LogLevel level,
            string category,
            string message,
            Exception? exception = null,
            EventId eventId = default,
            DateTimeOffset? timestamp = null)
        {
            lock (_gate)
            {
                Entries.Add((level, category, message));
            }
        }

        public void Flush()
        {
            FlushCalls++;
        }
    }
}
