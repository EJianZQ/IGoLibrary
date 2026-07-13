using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class GlobalLeakSettingsPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary.Ex.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SqliteSettingsRepository_RoundTripsGlobalLeakPriorityOrder()
    {
        var locations = new StorageLocations(_directory, Path.Combine(_directory, "logs"));
        var connectionFactory = new SqliteConnectionFactory(locations);
        await new SqliteAppDataInitializer(connectionFactory).InitializeAsync();
        var repository = new SqliteSettingsRepository(connectionFactory, new TestAppSettingsDefaults());
        var settings = AppSettings.Default with
        {
            Tasks = AppSettings.Default.Tasks with
            {
                GlobalLeak = new GlobalLeakTaskSettings(
                [
                    new GlobalLeakLibrarySelectionSettings(2, "场馆B", "5层"),
                    new GlobalLeakLibrarySelectionSettings(1, "场馆A", "3层"),
                    new GlobalLeakLibrarySelectionSettings(3, "场馆C", "7层")
                ])
            }
        };

        await repository.SaveAsync(settings);
        var restored = await repository.LoadAsync();

        Assert.Equal([2, 1, 3], restored.Tasks.GlobalLeak.SelectedLibraries.Select(item => item.LibraryId).ToArray());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TestAppSettingsDefaults : IAppSettingsDefaults
    {
        public AppSettings CreateDefault() => AppSettings.Default;
    }
}
