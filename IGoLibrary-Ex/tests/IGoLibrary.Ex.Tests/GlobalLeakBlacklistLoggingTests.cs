using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakBlacklistLoggingTests
{
    [Fact]
    public async Task SaveAndFilter_WriteStructuredCountsAndStableEvents()
    {
        var saveLogger = new CaptureLogger<GlobalLeakSeatBlacklistService>();
        var runLogger = new CaptureLogger<GlobalLeakWorkflowRunner>();
        using var fixture = new GlobalLeakBlacklistExecutionTests.Fixture(runLogger, saveLogger);
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] });
        await fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>> { [1] = [new("a", "1")] });
        Assert.Equal(3102, Assert.Single(saveLogger.Entries).Id.Id);
        Assert.Equal(1, saveLogger.Entries[0].Fields["AddedCount"]);
        fixture.Api.OnGetLibraryLayoutAsync = (_, id, _) => Task.FromResult(GlobalLeakBlacklistExecutionTests.Layout(id,
            new("a", "1", false, 0, 0), new("b", "2", false, 1, 0)));
        fixture.Api.OnReserveSeatAsync = (_, _, _, _) => Task.FromResult(true);
        await fixture.Coordinator.StartAsync(GlobalLeakBlacklistExecutionTests.Plan());
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var filtered = Assert.Single(runLogger.Entries);
        Assert.Equal(3103, filtered.Id.Id);
        Assert.Equal(2, filtered.Fields["AvailableCount"]);
        Assert.Equal(1, filtered.Fields["ExcludedCount"]);
        Assert.Equal(1, filtered.Fields["CandidateCount"]);
        Assert.DoesNotContain("cookie", filtered.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveFailure_RecordsException_WithoutSuccessEvent()
    {
        var logger = new CaptureLogger<GlobalLeakSeatBlacklistService>();
        using var fixture = new GlobalLeakBlacklistExecutionTests.Fixture(blacklistLogger: logger);
        var failure = new IOException("写入失败");
        fixture.Settings.UpdateExceptions.Enqueue(failure);
        await Assert.ThrowsAsync<IOException>(() => fixture.Blacklist.SaveAsync(new Dictionary<int, IReadOnlyList<SeatReference>>
        { [1] = [new("a", "1")] }));
        var entry = Assert.Single(logger.Entries);
        Assert.Same(failure, entry.Exception);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("保存全域捡漏黑名单失败", entry.Message);
        Assert.DoesNotContain(fixture.Log.Entries, entry => entry.Message.Contains("黑名单已保存"));
    }

    private sealed record CapturedLog(LogLevel Level, EventId Id, string Message,
        Exception? Exception, Dictionary<string, object?> Fields);
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add(new(logLevel, eventId,
                formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary()));
    }
}
