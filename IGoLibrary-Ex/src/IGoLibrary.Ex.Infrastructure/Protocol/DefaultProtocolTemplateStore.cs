using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Protocol;

public sealed class DefaultProtocolTemplateStore(
    SqliteConnectionFactory connectionFactory,
    ISettingsService settingsService,
    IPersistentDataChangeTracker? changeTracker = null) : IProtocolTemplateStore
{
    private const string OverridesKey = "protocol-overrides";
    private static readonly TraceIntProtocolTemplates DefaultTemplates =
        TraceIntProtocolValidator.Normalize(DefaultTraceIntProtocolTemplates.Instance);

    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _cacheLoadGate = new(1, 1);
    private TraceIntProtocolTemplates? _cachedEditableTemplates;
    private long _cachedEditableTemplatesVersion = -1;

    public Task<TraceIntProtocolTemplates> GetDefaultTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DefaultTemplates);
    }

    public async Task<TraceIntProtocolTemplates> GetEffectiveTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        if (!settings.TraceIntProtocol.GraphQlOverridesEnabled)
        {
            return DefaultTemplates;
        }

        return await GetEditableTemplatesAsync(cancellationToken);
    }

    public async Task<TraceIntProtocolTemplates> GetEditableTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (changeTracker is null)
        {
            return Merge(DefaultTemplates, await LoadOverridesAsync(cancellationToken));
        }

        var version = changeTracker.Version;
        if (TryGetCachedEditableTemplates(version, out var cached))
        {
            return cached;
        }

        await _cacheLoadGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                version = changeTracker.Version;
                if (TryGetCachedEditableTemplates(version, out cached))
                {
                    return cached;
                }

                var templates = Merge(DefaultTemplates, await LoadOverridesAsync(cancellationToken));
                if (changeTracker.Version != version)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                lock (_cacheGate)
                {
                    _cachedEditableTemplates = templates;
                    _cachedEditableTemplatesVersion = version;
                }

                return templates;
            }
        }
        finally
        {
            _cacheLoadGate.Release();
        }
    }

    public async Task SaveOverridesAsync(
        TraceIntProtocolTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var normalizedOverrides = NormalizeLegacyOverrides(TraceIntProtocolValidator.Normalize(overrides));
        var editableTemplates = Merge(DefaultTemplates, normalizedOverrides);
        TraceIntProtocolValidator.EnsureValid(editableTemplates);
        var sparseOverrides = TraceIntProtocolTemplateOverrides.FromDifferences(editableTemplates, DefaultTemplates);
        var json = JsonSerializer.Serialize(sparseOverrides, AppJson.Default);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ProtocolOverrides(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", OverridesKey);
        command.Parameters.AddWithValue("$value", json);
        await command.ExecuteNonQueryAsync(cancellationToken);
        changeTracker?.MarkChanged();
        InvalidateCache();
    }

    public async Task ResetOverridesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", OverridesKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
        changeTracker?.MarkChanged();
        InvalidateCache();
    }

    private bool TryGetCachedEditableTemplates(
        long version,
        out TraceIntProtocolTemplates templates)
    {
        lock (_cacheGate)
        {
            if (_cachedEditableTemplates is not null &&
                _cachedEditableTemplatesVersion == version)
            {
                templates = _cachedEditableTemplates;
                return true;
            }
        }

        templates = null!;
        return false;
    }

    private void InvalidateCache()
    {
        lock (_cacheGate)
        {
            _cachedEditableTemplates = null;
            _cachedEditableTemplatesVersion = -1;
        }
    }

    private static TraceIntProtocolTemplates Merge(
        TraceIntProtocolTemplates defaults,
        TraceIntProtocolTemplateOverrides rawOverrides)
    {
        var overrides = NormalizeLegacyOverrides(rawOverrides);
        return TraceIntProtocolValidator.Normalize(defaults with
        {
            GetCookieUrlTemplate = overrides.GetCookieUrlTemplate ?? defaults.GetCookieUrlTemplate,
            CookieAuthorizationReturnUrl = overrides.CookieAuthorizationReturnUrl ?? defaults.CookieAuthorizationReturnUrl,
            GraphQlEndpointUrl = overrides.GraphQlEndpointUrl ?? defaults.GraphQlEndpointUrl,
            GraphQlDefaultRefererUrl = overrides.GraphQlDefaultRefererUrl ?? defaults.GraphQlDefaultRefererUrl,
            GraphQlDefaultOriginUrl = overrides.GraphQlDefaultOriginUrl ?? defaults.GraphQlDefaultOriginUrl,
            GraphQlTomorrowRefererUrl = overrides.GraphQlTomorrowRefererUrl ?? defaults.GraphQlTomorrowRefererUrl,
            GraphQlTomorrowOriginUrl = overrides.GraphQlTomorrowOriginUrl ?? defaults.GraphQlTomorrowOriginUrl,
            TomorrowReservationQueueUrlTemplate = overrides.TomorrowReservationQueueUrlTemplate ?? defaults.TomorrowReservationQueueUrlTemplate,
            RemoteCheckInAuthUrlTemplate = overrides.RemoteCheckInAuthUrlTemplate ?? defaults.RemoteCheckInAuthUrlTemplate,
            RemoteCheckInAuthorizationReturnUrl = overrides.RemoteCheckInAuthorizationReturnUrl ?? defaults.RemoteCheckInAuthorizationReturnUrl,
            RemoteCheckInAuthRefererUrl = overrides.RemoteCheckInAuthRefererUrl ?? defaults.RemoteCheckInAuthRefererUrl,
            RemoteCheckInDevicesEndpointUrl = overrides.RemoteCheckInDevicesEndpointUrl ?? defaults.RemoteCheckInDevicesEndpointUrl,
            RemoteCheckInTimeEndpointUrl = overrides.RemoteCheckInTimeEndpointUrl ?? defaults.RemoteCheckInTimeEndpointUrl,
            RemoteCheckInSignEndpointUrl = overrides.RemoteCheckInSignEndpointUrl ?? defaults.RemoteCheckInSignEndpointUrl,
            RemoteCheckInApiRefererUrl = overrides.RemoteCheckInApiRefererUrl ?? defaults.RemoteCheckInApiRefererUrl,
            QueryLibrariesTemplate = overrides.QueryLibrariesTemplate ?? defaults.QueryLibrariesTemplate,
            QueryLibraryLayoutTemplate = overrides.QueryLibraryLayoutTemplate ?? defaults.QueryLibraryLayoutTemplate,
            QueryLibraryRuleTemplate = overrides.QueryLibraryRuleTemplate ?? defaults.QueryLibraryRuleTemplate,
            QueryReservationInfoTemplate = overrides.QueryReservationInfoTemplate ?? defaults.QueryReservationInfoTemplate,
            ReserveSeatTemplate = overrides.ReserveSeatTemplate ?? defaults.ReserveSeatTemplate,
            CancelReservationTemplate = overrides.CancelReservationTemplate ?? defaults.CancelReservationTemplate,
            TomorrowReservationWarmUpTemplate = overrides.TomorrowReservationWarmUpTemplate ?? defaults.TomorrowReservationWarmUpTemplate,
            TomorrowReservationSaveTemplate = overrides.TomorrowReservationSaveTemplate ?? defaults.TomorrowReservationSaveTemplate,
            TomorrowReservationInfoTemplate = overrides.TomorrowReservationInfoTemplate ?? defaults.TomorrowReservationInfoTemplate
        });
    }

    private static TraceIntProtocolTemplateOverrides NormalizeLegacyOverrides(
        TraceIntProtocolTemplateOverrides overrides)
    {
        return string.Equals(
                overrides.GetCookieUrlTemplate,
                DefaultTraceIntProtocolTemplates.LegacyGetCookieUrlTemplate,
                StringComparison.Ordinal)
            ? overrides with
            {
                GetCookieUrlTemplate = DefaultTraceIntProtocolTemplates.Instance.GetCookieUrlTemplate
            }
            : overrides;
    }

    private async Task<TraceIntProtocolTemplateOverrides> LoadOverridesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", OverridesKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<TraceIntProtocolTemplateOverrides>(json, AppJson.Default)
                   ?? new TraceIntProtocolTemplateOverrides();
        }

        return new TraceIntProtocolTemplateOverrides();
    }
}
