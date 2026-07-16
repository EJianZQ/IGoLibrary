using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Infrastructure.Protocol;
using Microsoft.Data.Sqlite;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
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
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            QueryLibrariesTemplate = "override-libraries",
            ReserveSeatTemplate = "override-reserve"
        });

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal("override-libraries", effective.QueryLibrariesTemplate);
        Assert.Equal("override-reserve", effective.ReserveSeatTemplate);
        Assert.Equal(defaults.GetCookieUrlTemplate, effective.GetCookieUrlTemplate);
        Assert.Equal(defaults.QueryLibraryRuleTemplate, effective.QueryLibraryRuleTemplate);
        Assert.Equal(defaults.CancelReservationTemplate, effective.CancelReservationTemplate);
    }

    [Fact]
    public async Task SaveOverridesAsync_CompactsFullEditableSnapshotToSparseOverrides()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var defaults = await store.GetDefaultTemplatesAsync();
        const string customEndpoint = "https://proxy.example.com/graphql";

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            GetCookieUrlTemplate = defaults.GetCookieUrlTemplate,
            CookieAuthorizationReturnUrl = defaults.CookieAuthorizationReturnUrl,
            GraphQlEndpointUrl = customEndpoint,
            GraphQlDefaultRefererUrl = defaults.GraphQlDefaultRefererUrl,
            GraphQlDefaultOriginUrl = defaults.GraphQlDefaultOriginUrl + "/",
            GraphQlTomorrowRefererUrl = defaults.GraphQlTomorrowRefererUrl,
            GraphQlTomorrowOriginUrl = defaults.GraphQlTomorrowOriginUrl,
            TomorrowReservationQueueUrlTemplate = defaults.TomorrowReservationQueueUrlTemplate,
            RemoteCheckInAuthUrlTemplate = defaults.RemoteCheckInAuthUrlTemplate,
            RemoteCheckInAuthorizationReturnUrl = defaults.RemoteCheckInAuthorizationReturnUrl,
            RemoteCheckInAuthRefererUrl = defaults.RemoteCheckInAuthRefererUrl,
            RemoteCheckInDevicesEndpointUrl = defaults.RemoteCheckInDevicesEndpointUrl,
            RemoteCheckInTimeEndpointUrl = defaults.RemoteCheckInTimeEndpointUrl,
            RemoteCheckInSignEndpointUrl = defaults.RemoteCheckInSignEndpointUrl,
            RemoteCheckInApiRefererUrl = defaults.RemoteCheckInApiRefererUrl,
            QueryLibrariesTemplate = defaults.QueryLibrariesTemplate,
            QueryLibraryLayoutTemplate = defaults.QueryLibraryLayoutTemplate,
            QueryLibraryRuleTemplate = defaults.QueryLibraryRuleTemplate,
            QueryReservationInfoTemplate = defaults.QueryReservationInfoTemplate,
            ReserveSeatTemplate = defaults.ReserveSeatTemplate,
            CancelReservationTemplate = defaults.CancelReservationTemplate,
            TomorrowReservationWarmUpTemplate = defaults.TomorrowReservationWarmUpTemplate,
            TomorrowReservationSaveTemplate = defaults.TomorrowReservationSaveTemplate,
            TomorrowReservationInfoTemplate = defaults.TomorrowReservationInfoTemplate
        });

        using var document = JsonDocument.Parse(await LoadRawOverridesJsonAsync());
        var properties = document.RootElement.EnumerateObject().ToArray();

        var savedProperty = Assert.Single(properties);
        Assert.Equal("graphQlEndpointUrl", savedProperty.Name);
        Assert.Equal(customEndpoint, savedProperty.Value.GetString());
        Assert.Equal(customEndpoint, (await store.GetEditableTemplatesAsync()).GraphQlEndpointUrl);
    }

    [Fact]
    public async Task SaveOverridesAsync_MergesTomorrowReservationOverridesWithDefaults()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            TomorrowReservationQueueUrlTemplate = "wss://override.example.com/ws?ns=prereserve/queue",
            TomorrowReservationWarmUpTemplate = "override-warm-up",
            TomorrowReservationSaveTemplate = "override-save",
            TomorrowReservationInfoTemplate = "override-info"
        });

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal("wss://override.example.com/ws?ns=prereserve/queue", effective.TomorrowReservationQueueUrlTemplate);
        Assert.Equal("override-warm-up", effective.TomorrowReservationWarmUpTemplate);
        Assert.Equal("override-save", effective.TomorrowReservationSaveTemplate);
        Assert.Equal("override-info", effective.TomorrowReservationInfoTemplate);
        Assert.Equal(defaults.QueryLibrariesTemplate, effective.QueryLibrariesTemplate);
        Assert.Equal(defaults.ReserveSeatTemplate, effective.ReserveSeatTemplate);
    }

    [Fact]
    public async Task GetEffectiveTemplatesAsync_UsesDefaultsForTomorrowFields_WhenSavedJsonIsFromOldVersion()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await SaveRawOverridesJsonAsync("""
            {
              "queryLibrariesTemplate": "legacy-libraries",
              "reserveSeatTemplate": "legacy-reserve"
            }
            """);

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal("legacy-libraries", effective.QueryLibrariesTemplate);
        Assert.Equal("legacy-reserve", effective.ReserveSeatTemplate);
        Assert.Equal(defaults.TomorrowReservationQueueUrlTemplate, effective.TomorrowReservationQueueUrlTemplate);
        Assert.Equal(defaults.TomorrowReservationWarmUpTemplate, effective.TomorrowReservationWarmUpTemplate);
        Assert.Equal(defaults.TomorrowReservationSaveTemplate, effective.TomorrowReservationSaveTemplate);
        Assert.Equal(defaults.TomorrowReservationInfoTemplate, effective.TomorrowReservationInfoTemplate);
    }

    [Fact]
    public async Task ResetOverridesAsync_RestoresDefaults()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            QueryReservationInfoTemplate = "temporary"
        });
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
            TraceIntProtocol = new TraceIntProtocolSettings(false)
        });
        var defaults = await store.GetEffectiveTemplatesAsync();

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            GetCookieUrlTemplate = "https://override.example.com/ReplaceMeByCode",
            QueryLibrariesTemplate = "override-libraries"
        });

        var effective = await store.GetEffectiveTemplatesAsync();
        var editable = await store.GetEditableTemplatesAsync();

        Assert.Equal(defaults.GetCookieUrlTemplate, effective.GetCookieUrlTemplate);
        Assert.Equal(defaults.QueryLibrariesTemplate, effective.QueryLibrariesTemplate);
        Assert.Equal("https://override.example.com/ReplaceMeByCode", editable.GetCookieUrlTemplate);
        Assert.Equal("override-libraries", editable.QueryLibrariesTemplate);
    }

    [Fact]
    public async Task SaveOverridesAsync_MergesAllProtocolAddressOverrides()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });

        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            GraphQlEndpointUrl = "https://proxy.example.com/graphql/",
            GraphQlDefaultRefererUrl = "https://proxy.example.com/app",
            GraphQlDefaultOriginUrl = "https://proxy.example.com/",
            GraphQlTomorrowRefererUrl = "https://proxy.example.com/tomorrow",
            GraphQlTomorrowOriginUrl = "https://proxy.example.com/",
            RemoteCheckInAuthUrlTemplate = "https://proxy.example.com/auth?r=ReplaceMeByReturnUrl&code=ReplaceMeByCode",
            RemoteCheckInAuthorizationReturnUrl = "https://proxy.example.com/return",
            RemoteCheckInAuthRefererUrl = "https://proxy.example.com/oauth",
            RemoteCheckInDevicesEndpointUrl = "https://proxy.example.com/devices",
            RemoteCheckInTimeEndpointUrl = "https://proxy.example.com/time",
            RemoteCheckInSignEndpointUrl = "https://proxy.example.com/sign",
            RemoteCheckInApiRefererUrl = "https://proxy.example.com/mini-program"
        });

        var effective = await store.GetEffectiveTemplatesAsync();

        Assert.Equal("https://proxy.example.com/graphql/", effective.GraphQlEndpointUrl);
        Assert.Equal("https://proxy.example.com", effective.GraphQlDefaultOriginUrl);
        Assert.Equal("https://proxy.example.com", effective.GraphQlTomorrowOriginUrl);
        Assert.Equal("https://proxy.example.com/auth?r=ReplaceMeByReturnUrl&code=ReplaceMeByCode", effective.RemoteCheckInAuthUrlTemplate);
        Assert.Equal("https://proxy.example.com/devices", effective.RemoteCheckInDevicesEndpointUrl);
        Assert.Equal("https://proxy.example.com/sign", effective.RemoteCheckInSignEndpointUrl);
    }

    [Fact]
    public async Task GetEditableTemplatesAsync_UpgradesLegacyBuiltInCookieTemplate()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        await SaveRawOverridesJsonAsync("""
            {
              "getCookieUrlTemplate": "http://wechat.v2.traceint.com/index.php/urlNew/auth.html?r=https%3A%2F%2Fweb.traceint.com%2Fweb%2Findex.html&code=ReplaceMeByCode&state=1"
            }
            """);

        var editable = await store.GetEditableTemplatesAsync();

        Assert.Contains(TraceIntProtocolValidator.ReturnUrlPlaceholder, editable.GetCookieUrlTemplate);
        Assert.Equal("https://web.traceint.com/web/index.html", editable.CookieAuthorizationReturnUrl);
    }

    [Fact]
    public async Task SaveOverridesAsync_RejectsInvalidAddressWithoutReplacingStoredValue()
    {
        var store = await CreateStoreAsync(AppSettings.Default with
        {
            TraceIntProtocol = new TraceIntProtocolSettings(true)
        });
        await store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
        {
            GraphQlEndpointUrl = "https://valid.example.com/graphql"
        });

        await Assert.ThrowsAsync<TraceIntProtocolValidationException>(() =>
            store.SaveOverridesAsync(new TraceIntProtocolTemplateOverrides
            {
                GraphQlEndpointUrl = "ftp://invalid.example.com/graphql"
            }));

        var editable = await store.GetEditableTemplatesAsync();
        Assert.Equal("https://valid.example.com/graphql", editable.GraphQlEndpointUrl);
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

    private static async Task SaveRawOverridesJsonAsync(string json)
    {
        var connectionFactory = new SqliteConnectionFactory();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProtocolOverrides(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", "protocol-overrides");
        command.Parameters.AddWithValue("$value", json);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> LoadRawOverridesJsonAsync()
    {
        var connectionFactory = new SqliteConnectionFactory();
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", "protocol-overrides");
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }
}
