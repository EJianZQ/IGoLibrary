using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
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
    public void ActivityLogService_Write_ForwardsExceptionAndEventId()
    {
        var writer = new CollectingLogWriter();
        var service = new ActivityLogService(writer);
        var exception = new InvalidOperationException("接口调用失败");
        var eventId = new EventId(42, "ReservationFailed");

        service.Write(LogEntryKind.Error, "Grab", "抢座失败。", exception, eventId);

        var written = Assert.Single(writer.Entries);
        Assert.Same(exception, written.Exception);
        Assert.Equal(eventId, written.EventId);
    }

    [Fact]
    public void ActivityLogService_Write_PublishesUiEntryBeforeForwardingToSharedWriter()
    {
        var writer = new CollectingLogWriter();
        var service = new ActivityLogService(writer);
        var writerEntryCountDuringNotification = -1;
        service.EntryWritten += (_, _) => writerEntryCountDuringNotification = writer.Entries.Count;

        service.Write(LogEntryKind.Info, "Grab", "任务状态已更新。");

        Assert.Equal(0, writerEntryCountDuringNotification);
        Assert.Single(writer.Entries);
    }

    [Fact]
    public void ActivityLogService_Write_RedactsSensitiveValuesBeforePublishingUiEntry()
    {
        var writer = new CollectingLogWriter();
        var service = new ActivityLogService(writer);

        service.Write(
            LogEntryKind.Error,
            "Auth",
            @"请求 https://example.test/callback?token=query-secret | {""token"":""json-secret with spaces""}");

        var entry = Assert.Single(service.Entries);
        Assert.DoesNotContain("query-secret", entry.Message);
        Assert.DoesNotContain("json-secret", entry.Message);
        Assert.Contains("<redacted>", entry.Message);

        var written = Assert.Single(writer.Entries);
        Assert.Equal(entry.Message, written.Message);
    }

    [Fact]
    public void ActivityLogService_Write_IsolatesFailingSubscribers_AndNotifiesRemainingSubscribers()
    {
        var writer = new CollectingLogWriter();
        var service = new ActivityLogService(writer);
        var successfulSubscriberCalls = 0;
        service.EntryWritten += (_, _) => throw new InvalidOperationException("订阅者失败");
        service.EntryWritten += (_, _) => successfulSubscriberCalls++;

        service.Write(LogEntryKind.Info, "Grab", "任务状态已更新。");

        Assert.Equal(1, successfulSubscriberCalls);
        Assert.Collection(
            writer.Entries,
            entry => Assert.Equal("Activity.Grab", entry.Category),
            entry =>
            {
                Assert.Equal("ActivityLog", entry.Category);
                Assert.IsType<InvalidOperationException>(entry.Exception);
                Assert.Equal(1001, entry.EventId.Id);
            });
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
    public void AppLogFileWriter_Write_CreatesRunLogFile_WithStructuredLine()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 9, 30, 15, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(
            LogLevel.Error,
            "Activity.Occupy",
            "重新预约失败。",
            new InvalidOperationException("接口返回失败"));

        writer.Dispose();

        var logFile = Path.Combine(_tempDirectory, "app-20260420-093015-000.log");
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

        var logFile = Path.Combine(_tempDirectory, "app-20260420-100000-000.log");
        var lines = File.ReadAllLines(logFile);

        var line = Assert.Single(lines);
        Assert.Contains("第一行\\n第二行\\n第三行", line);
    }

    [Fact]
    public void AppLogFileWriter_Write_RedactsSecretsUrlsAccountsAndUserPaths()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 10, 5, 0, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(
            LogLevel.Error,
            "Security",
            @"GET https://example.test/callback?token=query-secret | https://alice:uri-secret@example.test/file | Cookie: sid=cookie-secret | Authorization: Bearer auth-secret | Proxy-Authorization: Basic proxy-secret | Bearer loose-secret | token=named-secret | {""access_token"":""json-secret with spaces""} | alice@example.com | C:\Users\Alice Doe\config.json | /Users/alice/config.json",
            new InvalidOperationException("password=hunter2 | /home/alice/config.json"));
        writer.Flush();
        writer.Dispose();

        var logFile = Assert.Single(Directory.GetFiles(_tempDirectory, "app-*.log"));
        var content = File.ReadAllText(logFile);
        Assert.DoesNotContain("query-secret", content);
        Assert.DoesNotContain("uri-secret", content);
        Assert.DoesNotContain("cookie-secret", content);
        Assert.DoesNotContain("auth-secret", content);
        Assert.DoesNotContain("proxy-secret", content);
        Assert.DoesNotContain("loose-secret", content);
        Assert.DoesNotContain("named-secret", content);
        Assert.DoesNotContain("json-secret", content);
        Assert.DoesNotContain("hunter2", content);
        Assert.DoesNotContain("alice@example.com", content);
        Assert.DoesNotContain(@"\Alice Doe\", content);
        Assert.DoesNotContain("/Users/alice/", content);
        Assert.DoesNotContain("/home/alice/", content);
        Assert.Contains("<redacted>", content);
        Assert.Contains("***@example.com", content);
        Assert.Contains(@"%USERPROFILE%\config.json", content);
        Assert.Contains("/Users/<user>/config.json", content);
        Assert.Contains("/home/<user>/config.json", content);
    }

    [Fact]
    public void HostLogging_RegistersOnlyAppFileProvider_AndWritesEachEventOnce()
    {
        var writer = new CollectingLogWriter();
        var services = new ServiceCollection();
        services.AddSingleton<IAppLogWriter>(writer);
        services.AddLogging(HostBuilderFactory.ConfigureApplicationLogging);
        using var serviceProvider = services.BuildServiceProvider();

        var provider = Assert.Single(serviceProvider.GetServices<ILoggerProvider>());
        Assert.IsType<AppFileLoggerProvider>(provider);

        var logger = serviceProvider.GetRequiredService<ILogger<LoggingTests>>();
        logger.LogInformation("日志管线回归测试。");

        Assert.Single(writer.Entries);
    }

    [Fact]
    public void AppFileLoggerProvider_IncludesScopesAndEventId()
    {
        var writer = new CollectingLogWriter();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new AppFileLoggerProvider(writer));
        });
        var logger = loggerFactory.CreateLogger("Coordinator");

        using (logger.BeginScope("RunId={RunId}", "run-42"))
        {
            logger.LogInformation(new EventId(2001, "RunStarted"), "任务已启动。");
        }

        var entry = Assert.Single(writer.Entries);
        Assert.Contains("作用域=RunId=run-42", entry.Message);
        Assert.Equal(2001, entry.EventId.Id);
        Assert.Equal("RunStarted", entry.EventId.Name);
    }

    [Fact]
    public void AppLogFileWriter_WriteFailure_ReportsUnhealthyThenRecovery()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 10, 10, 0, TimeSpan.FromHours(8));
        var writeAttempts = 0;
        var healthChanges = new List<AppLogWriterHealthChangedEventArgs>();
        using var writer = new AppLogFileWriter(
            _tempDirectory,
            retainedFileCount: 14,
            clock: () => timestamp,
            queueCapacity: 16,
            beforeWriteAsync: () =>
            {
                if (Interlocked.Increment(ref writeAttempts) == 1)
                {
                    throw new IOException("模拟日志写入失败");
                }

                return Task.CompletedTask;
            });
        writer.HealthChanged += (_, args) => healthChanges.Add(args);

        writer.Write(LogLevel.Error, "Logging", "第一次写入");
        writer.Flush();
        Assert.False(writer.IsHealthy);
        Assert.Equal(1, writer.ConsecutiveFailureCount);

        writer.Write(LogLevel.Error, "Logging", "第二次写入");
        writer.Flush();

        Assert.True(writer.IsHealthy);
        Assert.Equal(0, writer.ConsecutiveFailureCount);
        Assert.Collection(
            healthChanges,
            failed =>
            {
                Assert.False(failed.IsHealthy);
                Assert.Equal("写入日志文件", failed.Operation);
                Assert.Contains("模拟日志写入失败", failed.ErrorMessage);
            },
            recovered =>
            {
                Assert.True(recovered.IsHealthy);
                Assert.Equal("写入日志文件", recovered.Operation);
            });
    }

    [Fact]
    public void AppLogFileWriter_CleanupOldFiles_UsesRunTimestampInsteadOfCreationOrder()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260420-080000-000.log"), "newest");
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260418-080000-000.log"), "oldest");
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260419-080000-000.log"), "middle");

        using var writer = new AppLogFileWriter(
            _tempDirectory,
            retainedFileCount: 2,
            clock: () => new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.FromHours(8)));

        writer.Write(LogLevel.Information, "Global", "cleanup-trigger");
        writer.Dispose();

        Assert.True(File.Exists(Path.Combine(_tempDirectory, "app-20260421-090000-000.log")));
        Assert.True(File.Exists(Path.Combine(_tempDirectory, "app-20260420-080000-000.log")));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "app-20260419-080000-000.log")));
        Assert.False(File.Exists(Path.Combine(_tempDirectory, "app-20260418-080000-000.log")));
    }

    [Fact]
    public async Task AppLogFileWriter_WhenQueueIsBounded_RecordsDroppedEntryWarning()
    {
        var timestamp = new DateTimeOffset(2026, 4, 21, 8, 0, 0, TimeSpan.FromHours(8));
        var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrites = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var writer = new AppLogFileWriter(
            _tempDirectory,
            retainedFileCount: 14,
            clock: () => timestamp,
            queueCapacity: 16,
            beforeWriteAsync: async () =>
            {
                processingStarted.TrySetResult();
                await releaseWrites.Task;
            });

        try
        {
            writer.Write(LogLevel.Information, "Grab", "日志 0");
            await processingStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (var index = 1; index < 256; index++)
            {
                writer.Write(LogLevel.Information, "Grab", $"日志 {index}");
            }
        }
        finally
        {
            releaseWrites.TrySetResult();
        }

        writer.Flush();
        writer.Dispose();

        var logFile = Path.Combine(_tempDirectory, "app-20260421-080000-000.log");
        var content = File.ReadAllText(logFile);

        Assert.Contains("日志队列已满，已丢弃", content);
    }

    [Fact]
    public void AppLogFileWriter_RunAcrossMidnight_ContinuesUsingStartupFile()
    {
        var now = new DateTimeOffset(2026, 4, 20, 23, 59, 59, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => now);

        writer.Write(LogLevel.Information, "Bootstrap", "before-midnight");
        now = now.AddMinutes(2);
        writer.Write(LogLevel.Information, "Bootstrap", "after-midnight");
        writer.Dispose();

        var logFile = Assert.Single(Directory.GetFiles(_tempDirectory, "app-*.log"));
        Assert.Equal("app-20260420-235959-000.log", Path.GetFileName(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("before-midnight", content);
        Assert.Contains("after-midnight", content);
    }

    [Fact]
    public async Task AppLogFileWriter_DisableAndReEnable_AppliesImmediatelyAndUsesSameFile()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 9, 30, 15, TimeSpan.FromHours(8));
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(LogLevel.Information, "Logging", "before-disable");
        await writer.ApplyAsync(new LogFileSettings(false, 30));
        writer.Write(LogLevel.Information, "Logging", "while-disabled");
        await writer.ApplyAsync(new LogFileSettings(true, 30));
        writer.Write(LogLevel.Information, "Logging", "after-enable");
        writer.Dispose();

        var logFile = Assert.Single(Directory.GetFiles(_tempDirectory, "app-*.log"));
        var content = File.ReadAllText(logFile);
        Assert.Contains("before-disable", content);
        Assert.DoesNotContain("while-disabled", content);
        Assert.Contains("after-enable", content);
    }

    [Fact]
    public async Task AppLogFileWriter_WhenDisabledBeforeStartupLogs_DoesNotCreateLogFile()
    {
        var locations = new StorageLocations(_tempDirectory, _tempDirectory);
        using var writer = new AppLogFileWriter(locations, startUnconfigured: true);

        writer.Write(LogLevel.Information, "Bootstrap", "buffered-before-settings");
        await writer.ApplyAsync(new LogFileSettings(false, 30));
        writer.Write(LogLevel.Information, "Bootstrap", "disabled");
        writer.Dispose();

        Assert.Empty(Directory.GetFiles(_tempDirectory, "app-*.log"));
    }

    [Fact]
    public void AppLogFileWriter_WhenStartupNameExists_UsesCollisionSuffix()
    {
        var timestamp = new DateTimeOffset(2026, 4, 20, 9, 30, 15, TimeSpan.FromHours(8));
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "app-20260420-093015-000.log"), "existing");
        using var writer = new AppLogFileWriter(_tempDirectory, clock: () => timestamp);

        writer.Write(LogLevel.Information, "Bootstrap", "new-run");
        writer.Dispose();

        Assert.Equal("existing", File.ReadAllText(Path.Combine(_tempDirectory, "app-20260420-093015-000.log")));
        Assert.Contains("new-run", File.ReadAllText(Path.Combine(_tempDirectory, "app-20260420-093015-000-01.log")));
    }

    [Fact]
    public async Task AppLogFileWriter_DeletesOnlyExactLegacyDailyLogs()
    {
        Directory.CreateDirectory(_tempDirectory);
        var legacy = Path.Combine(_tempDirectory, "app-20260420.log");
        var unrelated = Path.Combine(_tempDirectory, "app-backup.log");
        var similar = Path.Combine(_tempDirectory, "app-20260420-extra.log");
        File.WriteAllText(legacy, "legacy");
        File.WriteAllText(unrelated, "unrelated");
        File.WriteAllText(similar, "similar");
        var locations = new StorageLocations(_tempDirectory, _tempDirectory);
        using var writer = new AppLogFileWriter(locations, startUnconfigured: true);

        await writer.ApplyAsync(new LogFileSettings(false, 30));
        writer.Dispose();

        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(similar));
    }

    [Fact]
    public async Task AppLogFileWriter_DisablingAlone_DoesNotDeleteExistingRunLogs()
    {
        Directory.CreateDirectory(_tempDirectory);
        var existing = new[]
        {
            Path.Combine(_tempDirectory, "app-20260418-080000-000.log"),
            Path.Combine(_tempDirectory, "app-20260419-080000-000.log"),
            Path.Combine(_tempDirectory, "app-20260420-080000-000.log")
        };
        foreach (var path in existing)
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }

        var locations = new StorageLocations(_tempDirectory, _tempDirectory);
        using var writer = new AppLogFileWriter(locations, startUnconfigured: true);
        await writer.ApplyAsync(new LogFileSettings(true, 365));
        await writer.ApplyAsync(new LogFileSettings(false, 365));
        writer.Dispose();

        Assert.All(existing, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task AppLogFileWriter_DecreasingRetention_ImmediatelyDeletesOldestRunLogs()
    {
        Directory.CreateDirectory(_tempDirectory);
        var oldest = Path.Combine(_tempDirectory, "app-20260418-080000-000.log");
        var middle = Path.Combine(_tempDirectory, "app-20260419-080000-000.log");
        var newest = Path.Combine(_tempDirectory, "app-20260420-080000-000.log");
        File.WriteAllText(oldest, "oldest");
        File.WriteAllText(middle, "middle");
        File.WriteAllText(newest, "newest");
        var locations = new StorageLocations(_tempDirectory, _tempDirectory);
        using var writer = new AppLogFileWriter(locations, startUnconfigured: true);
        await writer.ApplyAsync(new LogFileSettings(false, 3));

        await writer.ApplyAsync(new LogFileSettings(false, 2));

        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(middle));
        Assert.True(File.Exists(newest));
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

        public List<(
            LogLevel Level,
            string Category,
            string Message,
            Exception? Exception,
            EventId EventId)> Entries { get; } = [];

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
                Entries.Add((level, category, message, exception, eventId));
            }
        }

        public void Flush()
        {
            FlushCalls++;
        }
    }
}
