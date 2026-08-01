using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace IGoLibrary.Ex.Tests;

public sealed class SqliteTaskLaunchHistoryRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-TaskLaunchHistoryTests",
        Guid.NewGuid().ToString("N"));
    private SqliteConnectionFactory? _repositoryFactory;

    [Fact]
    public async Task Initializer_UpgradesLegacyDatabaseWithHistoryTable()
    {
        _repositoryFactory = new SqliteConnectionFactory(new StorageLocations(
            Path.Combine(_root, "data"),
            Path.Combine(_root, "logs")));
        await using (var connection = _repositoryFactory.Create())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE Settings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                CREATE TABLE Favorites (LibraryId INTEGER NOT NULL, SeatKey TEXT NOT NULL, SeatName TEXT NOT NULL, PRIMARY KEY (LibraryId, SeatKey));
                CREATE TABLE ProtocolOverrides (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteAppDataInitializer(_repositoryFactory).InitializeAsync();

        await using var verification = _repositoryFactory.Create();
        await verification.OpenAsync();
        var tableCommand = verification.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MobileTaskLaunchHistory';";
        Assert.Equal(1L, await tableCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task RecordGrabAsync_DeduplicatesByEffectiveConfigurationAndRefreshesNames()
    {
        var (repository, service, timeProvider) = await CreateAsync();
        var first = await service.RecordGrabAsync(CreateGrabPlan("电子阅览室A", "27", new TimeOnly(20, 0)));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var refreshed = await service.RecordGrabAsync(CreateGrabPlan("电子阅览室A（新名称）", "27号", null));

        Assert.False(first.RefreshedExisting);
        Assert.True(refreshed.RefreshedExisting);
        Assert.Equal(first.RecordId, refreshed.RecordId);
        var record = Assert.Single(await repository.GetRecentGrabAsync());
        Assert.Equal("电子阅览室A（新名称）", record.LibraryName);
        Assert.Equal("27号", Assert.Single(record.Seats).SeatName);
        Assert.Equal(timeProvider.GetUtcNow(), record.RecordedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_KeepsFiveNewestRecordsPerKindWithStableSequenceOrdering()
    {
        var (repository, service, _) = await CreateAsync();
        for (var index = 1; index <= 6; index++)
        {
            await service.RecordGrabAsync(CreateGrabPlan($"场馆 {index}", index.ToString(), null, libraryId: index));
            await service.RecordGlobalLeakAsync(new GlobalLeakPlan(
                [new GlobalLeakLibraryTarget(index, $"全域场馆 {index}", $"{index}层")],
                TimeSpan.FromSeconds(10)));
        }

        var grab = await repository.GetRecentGrabAsync();
        var globalLeak = await repository.GetRecentGlobalLeakAsync();
        Assert.Equal(5, grab.Count);
        Assert.Equal([6, 5, 4, 3, 2], grab.Select(static record => record.LibraryId));
        Assert.Equal(5, globalLeak.Count);
        Assert.Equal([6, 5, 4, 3, 2], globalLeak.Select(static record => record.Libraries[0].LibraryId));
    }

    [Fact]
    public async Task RecordGlobalLeakAsync_TreatsPriorityOrderAndIntervalAsIdentity()
    {
        var (repository, service, _) = await CreateAsync();
        var libraries = new[]
        {
            new GlobalLeakLibraryTarget(1, "一层", "1F"),
            new GlobalLeakLibraryTarget(2, "二层", "2F")
        };

        await service.RecordGlobalLeakAsync(new GlobalLeakPlan(libraries, TimeSpan.FromSeconds(10)));
        await service.RecordGlobalLeakAsync(new GlobalLeakPlan(libraries.Reverse().ToArray(), TimeSpan.FromSeconds(10)));
        await service.RecordGlobalLeakAsync(new GlobalLeakPlan(libraries, TimeSpan.FromSeconds(11)));

        var records = await repository.GetRecentGlobalLeakAsync();
        Assert.Equal(3, records.Count);
        Assert.Equal(TimeSpan.FromSeconds(11), records[0].ScanInterval);
        Assert.Equal([2, 1], records[1].Libraries.Select(static library => library.LibraryId));
        Assert.Equal([1, 2], records[2].Libraries.Select(static library => library.LibraryId));
    }

    [Fact]
    public async Task RecordGrabAsync_TreatsSeatOrderAsIdentity()
    {
        var (repository, service, _) = await CreateAsync();
        var first = CreateGrabPlanWithSeats("seat-a", "seat-b");
        var reversed = CreateGrabPlanWithSeats("seat-b", "seat-a");

        await service.RecordGrabAsync(first);
        await service.RecordGrabAsync(reversed);

        var records = await repository.GetRecentGrabAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(["seat-b", "seat-a"], records[0].Seats.Select(static seat => seat.SeatKey));
        Assert.Equal(["seat-a", "seat-b"], records[1].Seats.Select(static seat => seat.SeatKey));
    }

    [Fact]
    public async Task RecordGlobalLeakAsync_RefreshesNamesWithoutChangingIdentity()
    {
        var (repository, service, timeProvider) = await CreateAsync();
        var first = await service.RecordGlobalLeakAsync(new GlobalLeakPlan(
            [new GlobalLeakLibraryTarget(1, "旧名称", "旧楼层")],
            TimeSpan.FromSeconds(10)));
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        var refreshed = await service.RecordGlobalLeakAsync(new GlobalLeakPlan(
            [new GlobalLeakLibraryTarget(1, "新名称", "新楼层")],
            TimeSpan.FromSeconds(10)));

        Assert.True(refreshed.RefreshedExisting);
        Assert.Equal(first.RecordId, refreshed.RecordId);
        var record = Assert.Single(await repository.GetRecentGlobalLeakAsync());
        Assert.Equal("新名称", Assert.Single(record.Libraries).LibraryName);
        Assert.Equal("新楼层", record.Libraries[0].Floor);
    }

    [Fact]
    public async Task GetRecentAsync_LogsSuccessfulPollingAtDebugLevel()
    {
        var logger = new CapturingLogger<SqliteTaskLaunchHistoryRepository>();
        var (repository, _, _) = await CreateAsync(logger);

        await repository.GetRecentGrabAsync();

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
    }

    [Fact]
    public async Task GetRecentAsync_SkipsCorruptPayloadWithoutHidingValidRecords()
    {
        var logger = new CapturingLogger<SqliteTaskLaunchHistoryRepository>();
        var (repository, service, _) = await CreateAsync(logger);
        await service.RecordGrabAsync(CreateGrabPlan("有效场馆", "27", null));

        await using (var connection = _repositoryFactory!.Create())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO MobileTaskLaunchHistory(RecordId, TaskKind, Fingerprint, RecordedAtUtc, PayloadJson)
                VALUES($recordId, 'grab', 'corrupt', $recordedAtUtc, '{not-json');
                """;
            command.Parameters.AddWithValue("$recordId", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$recordedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();

            command.Parameters.Clear();
            command.CommandText =
                """
                INSERT INTO MobileTaskLaunchHistory(RecordId, TaskKind, Fingerprint, RecordedAtUtc, PayloadJson)
                VALUES($recordId, 'grab', 'null-item', $recordedAtUtc, $payloadJson);
                """;
            command.Parameters.AddWithValue("$recordId", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("$recordedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue(
                "$payloadJson",
                """{"version":1,"libraryId":1,"libraryName":"损坏场馆","seats":[null],"pollingMode":0,"reservationStrategy":0}""");
            await command.ExecuteNonQueryAsync();
        }

        var record = Assert.Single(await repository.GetRecentGrabAsync());
        Assert.Equal("有效场馆", record.LibraryName);
        Assert.Contains(logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SaveAsync_RollsBackDedupeDeleteWhenReplacementInsertFails()
    {
        var (repository, service, _) = await CreateAsync();
        var original = await service.RecordGrabAsync(CreateGrabPlan("原名称", "27", null));
        await using (var connection = _repositoryFactory!.Create())
        {
            await connection.OpenAsync();
            var trigger = connection.CreateCommand();
            trigger.CommandText =
                """
                CREATE TRIGGER RejectHistoryInsert
                BEFORE INSERT ON MobileTaskLaunchHistory
                BEGIN
                    SELECT RAISE(ABORT, 'test insert failure');
                END;
                """;
            await trigger.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<SqliteException>(() =>
            service.RecordGrabAsync(CreateGrabPlan("新名称", "27", null)));

        var record = Assert.Single(await repository.GetRecentGrabAsync());
        Assert.Equal(original.RecordId, record.RecordId);
        Assert.Equal("原名称", record.LibraryName);
    }

    private async Task<(SqliteTaskLaunchHistoryRepository Repository, TaskLaunchHistoryService Service, FakeTimeProvider TimeProvider)> CreateAsync(
        ILogger<SqliteTaskLaunchHistoryRepository>? logger = null)
    {
        _repositoryFactory = new SqliteConnectionFactory(new StorageLocations(
            Path.Combine(_root, "data"),
            Path.Combine(_root, "logs")));
        await new SqliteAppDataInitializer(_repositoryFactory).InitializeAsync();
        var repository = new SqliteTaskLaunchHistoryRepository(
            _repositoryFactory,
            logger ?? NullLogger<SqliteTaskLaunchHistoryRepository>.Instance);
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 16, 8, 0, 0, TimeSpan.Zero));
        return (repository, new TaskLaunchHistoryService(repository, timeProvider), timeProvider);
    }

    private static GrabSeatPlan CreateGrabPlan(
        string libraryName,
        string seatName,
        TimeOnly? scheduledStart,
        int libraryId = 1)
    {
        return new GrabSeatPlan(
            libraryId,
            libraryName,
            [new SeatReference("seat-" + libraryId, seatName)],
            GrabPollingMode.Randomized,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Randomized),
            scheduledStart,
            GrabReservationStrategy.ReserveDirectly);
    }

    private static GrabSeatPlan CreateGrabPlanWithSeats(params string[] seatKeys)
    {
        return new GrabSeatPlan(
            1,
            "场馆",
            seatKeys.Select(static key => new SeatReference(key, key)).ToArray(),
            GrabPollingMode.Randomized,
            GrabPollingStrategyFactory.FromMode(GrabPollingMode.Randomized),
            null,
            GrabReservationStrategy.ReserveDirectly);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, exception));
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
