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

    public Task<TraceIntProtocolTemplates> GetDefaultTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TraceIntProtocolValidator.Normalize(DefaultTraceIntProtocolTemplates.Instance));
    }

    public async Task<TraceIntProtocolTemplates> GetEffectiveTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var defaults = await GetDefaultTemplatesAsync(cancellationToken);
        var settings = await settingsService.LoadAsync(cancellationToken);
        if (!settings.TraceIntProtocol.GraphQlOverridesEnabled)
        {
            return defaults;
        }

        return Merge(defaults, await LoadOverridesAsync(cancellationToken));
    }

    public async Task<TraceIntProtocolTemplates> GetEditableTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var defaults = await GetDefaultTemplatesAsync(cancellationToken);
        return Merge(defaults, await LoadOverridesAsync(cancellationToken));
    }

    public async Task SaveOverridesAsync(
        TraceIntProtocolTemplateOverrides overrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var defaults = await GetDefaultTemplatesAsync(cancellationToken);
        var normalizedOverrides = NormalizeLegacyOverrides(TraceIntProtocolValidator.Normalize(overrides));
        var editableTemplates = Merge(defaults, normalizedOverrides);
        TraceIntProtocolValidator.EnsureValid(editableTemplates);
        var sparseOverrides = TraceIntProtocolTemplateOverrides.FromDifferences(editableTemplates, defaults);
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
