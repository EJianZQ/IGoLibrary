using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Infrastructure.Logging;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingSettingsTests
{
    [Fact]
    public async Task LoggerFailureDoesNotTurnCommittedSettingsIntoSaveFailure()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var service = new LoggingSettingsWorkflowService(settings, new RuntimeWriter(), new FailingLogger());
        var result = await service.SaveAsync(LogFileSettings.Default with { RecordNetworkRequests = true });
        Assert.True(settings.CurrentSettings.Logging.RecordNetworkRequests);
        Assert.True(result.Settings.RecordNetworkRequests);
        Assert.Null(result.RuntimeResult.ApplicationFailure);
    }
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"logging\":{}}", false)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":null}}", false)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":\"true\"}}", false)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":1}}", false)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":{}}}", false)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":true}}", true)]
    [InlineData("{\"logging\":{\"recordNetworkRequests\":false}}", false)]
    public void MigratesAndRoundTripsWithoutRepeatedMigration(string json, bool expected)
    {
        var canonical = SqliteSettingsRepository.MigrateAppSettingsJson(json);
        var settings = JsonSerializer.Deserialize<AppSettings>(canonical, AppJson.Default)!;
        Assert.Equal(expected, settings.Logging.RecordNetworkRequests);
        var saved = JsonSerializer.Serialize(settings, AppJson.Default);
        Assert.Equal(saved, SqliteSettingsRepository.MigrateAppSettingsJson(saved));
        Assert.Equal(expected, JsonSerializer.Deserialize<AppSettings>(saved, AppJson.Default)!.Logging.RecordNetworkRequests);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task RuntimeHonorsBothSwitches(bool files, bool network, bool effective)
    {
        var state = new NetworkLogState();
        var writer = new RuntimeWriter();
        var runtime = new AppLogRuntimeController(writer, state);
        await runtime.ApplyAsync(new(files, 30) { RecordNetworkRequests = network });
        Assert.Equal(effective, state.CaptureGeneration() != 0);
        Assert.Equal(network, writer.Applied!.RecordNetworkRequests);
    }

    [Fact]
    public async Task RuntimeEnablesOnlyAfterWriterAppliesAndInvalidatesOldGeneration()
    {
        var state = new NetworkLogState();
        var pending = new TaskCompletionSource<LogRuntimeApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = new RuntimeWriter { Apply = _ => pending.Task };
        var runtime = new AppLogRuntimeController(writer, state);
        var task = runtime.ApplyAsync(LogFileSettings.Default with { RecordNetworkRequests = true });
        Assert.Equal(0, state.CaptureGeneration());
        pending.SetResult(LogRuntimeApplyResult.Success);
        await task;
        var old = state.CaptureGeneration();
        Assert.True(state.IsCurrent(old));
        await runtime.ApplyAsync(LogFileSettings.Default);
        Assert.False(state.IsCurrent(old));
        await runtime.ApplyAsync(LogFileSettings.Default with { RecordNetworkRequests = true });
        Assert.False(state.IsCurrent(old));
    }

    [Fact]
    public async Task RuntimeFailureKeepsCommittedValueAndDisablesCapture()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var writer = new RuntimeWriter { Apply = _ => throw new IOException("secret") };
        var state = new NetworkLogState();
        var workflow = new LoggingSettingsWorkflowService(settings, new AppLogRuntimeController(writer, state));
        var result = await workflow.SaveAsync(LogFileSettings.Default with { RecordNetworkRequests = true });
        Assert.True(result.Settings.RecordNetworkRequests);
        Assert.True(settings.CurrentSettings.Logging.RecordNetworkRequests);
        Assert.NotNull(result.RuntimeResult.ApplicationFailure);
        Assert.DoesNotContain("secret", result.RuntimeResult.ApplicationFailure);
        Assert.Equal(0, state.CaptureGeneration());
    }

    [Fact]
    public async Task PersistenceFailureDoesNotChangeGeneration()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        settings.UpdateExceptions.Enqueue(new IOException("unavailable"));
        var state = new NetworkLogState();
        var workflow = new LoggingSettingsWorkflowService(settings, new AppLogRuntimeController(new RuntimeWriter(), state));
        await Assert.ThrowsAsync<IOException>(() => workflow.SaveAsync(LogFileSettings.Default with { RecordNetworkRequests = true }));
        Assert.Equal(0, state.CaptureGeneration());
    }

    [Fact]
    public async Task SqliteRestartPreservesPreferenceAndSchemaVersion()
    {
        var directory = Directory.CreateTempSubdirectory("network-settings-");
        try
        {
            var factory = new SqliteConnectionFactory(new StorageLocations(directory.FullName, directory.FullName));
            await new SqliteAppDataInitializer(factory).InitializeAsync();
            await using var connection = factory.Create();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            var version = await command.ExecuteScalarAsync();
            var repository = new SqliteSettingsRepository(factory, new Defaults());
            foreach (var enabled in new[] { true, false, true })
            {
                await repository.SaveAsync(AppSettings.Default with { Logging = new(false, 42) { RecordNetworkRequests = enabled } });
                var restarted = new SqliteSettingsRepository(factory, new Defaults());
                var loaded = await restarted.LoadAsync();
                Assert.Equal(enabled, loaded.Logging.RecordNetworkRequests);
                Assert.False(loaded.Logging.Enabled);
                Assert.Equal(42, loaded.Logging.RetainedFileCount);
                Assert.Equal(version, await command.ExecuteScalarAsync());
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); directory.Delete(true); }
    }

    [Fact]
    public async Task ConcurrentSavesCannotApplyOlderRuntimeStateAfterNewerPersistedState()
    {
        var settings = new FakeSettingsService(AppSettings.Default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<LogRuntimeApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new List<bool>();
        var writer = new RuntimeWriter
        {
            Apply = value =>
            {
                applied.Add(value.RecordNetworkRequests);
                if (applied.Count != 1) return Task.FromResult(LogRuntimeApplyResult.Success);
                entered.SetResult();
                return release.Task;
            }
        };
        var state = new NetworkLogState();
        var service = new LoggingSettingsWorkflowService(settings, new AppLogRuntimeController(writer, state));
        var first = service.SaveAsync(LogFileSettings.Default with { RecordNetworkRequests = true });
        await entered.Task;
        var second = service.SaveAsync(LogFileSettings.Default);
        Assert.True(settings.CurrentSettings.Logging.RecordNetworkRequests);
        release.SetResult(LogRuntimeApplyResult.Success);
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { true, false }, applied);
        Assert.False(settings.CurrentSettings.Logging.RecordNetworkRequests);
        Assert.Equal(0, state.CaptureGeneration());
    }

    [Fact]
    public async Task CancellationAfterCommitDoesNotSkipRuntimeApplication()
    {
        using var cancellation = new CancellationTokenSource();
        var settings = new FakeSettingsService(AppSettings.Default);
        var writer = new RuntimeWriter
        {
            Apply = _ =>
            {
                Assert.True(settings.CurrentSettings.Logging.RecordNetworkRequests);
                cancellation.Cancel();
                return Task.FromResult(LogRuntimeApplyResult.Success);
            }
        };
        var state = new NetworkLogState();
        var service = new LoggingSettingsWorkflowService(settings, new AppLogRuntimeController(writer, state));
        await service.SaveAsync(LogFileSettings.Default with { RecordNetworkRequests = true }, cancellation.Token);
        Assert.NotEqual(0, state.CaptureGeneration());
    }

    private sealed class Defaults : IAppSettingsDefaults { public AppSettings CreateDefault() => AppSettings.Default; }
    private sealed class FailingLogger : ILogger<LoggingSettingsWorkflowService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => throw new IOException("logger failure");
    }
    private sealed class RuntimeWriter : IAppLogWriter, IAppLogRuntimeController
    {
        public Func<LogFileSettings, Task<LogRuntimeApplyResult>> Apply { get; init; } = _ => Task.FromResult(LogRuntimeApplyResult.Success);
        public LogFileSettings? Applied { get; private set; }
        public Task<LogRuntimeApplyResult> ApplyAsync(LogFileSettings settings, CancellationToken cancellationToken = default)
        { Applied = settings; return Apply(settings); }
        public void Write(LogLevel level, string category, string message, Exception? exception = null, EventId eventId = default, DateTimeOffset? timestamp = null) { }
        public void Flush() { }
    }
}
