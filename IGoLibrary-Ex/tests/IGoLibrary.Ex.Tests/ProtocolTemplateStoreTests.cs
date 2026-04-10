using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Infrastructure.Protocol;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

public sealed class ProtocolTemplateStoreTests : IDisposable
{
    private const string DataDirEnvironmentVariable = "IGOLIBRARY_EX_DATA_DIR";
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-Tests",
        Guid.NewGuid().ToString("N"));

    public ProtocolTemplateStoreTests()
    {
        Environment.SetEnvironmentVariable(DataDirEnvironmentVariable, _dataDirectory);
    }

    [Fact]
    public async Task SaveOverridesAsync_MergesOverridesWithDefaults()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            CustomApiOverridesEnabled = true
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new ProtocolTemplateOverrides(
            QueryLibrariesTemplate: "override-libraries",
            ReserveSeatTemplate: "override-reserve"));

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal("override-libraries", effective.QueryLibrariesTemplate);
        Assert.Equal("override-reserve", effective.ReserveSeatTemplate);
        Assert.Equal(defaults.GetCookieUrlTemplate, effective.GetCookieUrlTemplate);
        Assert.Equal(defaults.QueryLibraryRuleTemplate, effective.QueryLibraryRuleTemplate);
        Assert.Equal(defaults.CancelReservationTemplate, effective.CancelReservationTemplate);
    }

    [Fact]
    public async Task ResetOverridesAsync_RestoresDefaults()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            CustomApiOverridesEnabled = true
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new ProtocolTemplateOverrides(QueryReservationInfoTemplate: "temporary"));
        await store.ResetOverridesAsync();

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal(defaults.QueryReservationInfoTemplate, effective.QueryReservationInfoTemplate);
        Assert.Equal(defaults.QueryLibraryRuleTemplate, effective.QueryLibraryRuleTemplate);
        Assert.Contains("ReplaceMeByCode", effective.GetCookieUrlTemplate);
    }

    [Fact]
    public async Task GetEffectiveTemplatesAsync_IgnoresSavedOverrides_WhenCustomApiOverridesAreDisabled()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            CustomApiOverridesEnabled = false
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new ProtocolTemplateOverrides(
            GetCookieUrlTemplate: "https://override.example.com/ReplaceMeByCode",
            QueryLibrariesTemplate: "override-libraries"));

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal(defaults.GetCookieUrlTemplate, effective.GetCookieUrlTemplate);
        Assert.Equal(defaults.QueryLibrariesTemplate, effective.QueryLibrariesTemplate);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DataDirEnvironmentVariable, null);
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_dataDirectory))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(_dataDirectory, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    private static async Task<DefaultProtocolTemplateStore> CreateStoreAsync(AppSettings? settings = null)
    {
        var connectionFactory = new SqliteConnectionFactory();
        var initializer = new SqliteAppDataInitializer(connectionFactory);
        await initializer.InitializeAsync();
        var settingsService = new FakeSettingsService(settings ?? AppSettings.Default);
        return new DefaultProtocolTemplateStore(connectionFactory, settingsService);
    }
}
