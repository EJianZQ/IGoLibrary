using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Protocol;

public sealed class DefaultProtocolTemplateStore(
    SqliteConnectionFactory connectionFactory,
    ISettingsService settingsService) : IProtocolTemplateStore
{
    private const string OverridesKey = "protocol-overrides";

    public async Task<TraceIntGraphQlTemplates> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var defaults = DefaultTraceIntGraphQlTemplates.Instance;
        var settings = await settingsService.LoadAsync(cancellationToken);
        if (!settings.TraceIntProtocol.GraphQlOverridesEnabled)
        {
            return defaults;
        }

        var overrides = await LoadOverridesAsync(cancellationToken);

        return new TraceIntGraphQlTemplates(
            overrides.GetCookieUrlTemplate ?? defaults.GetCookieUrlTemplate,
            overrides.QueryLibrariesTemplate ?? defaults.QueryLibrariesTemplate,
            overrides.QueryLibraryLayoutTemplate ?? defaults.QueryLibraryLayoutTemplate,
            overrides.QueryLibraryRuleTemplate ?? defaults.QueryLibraryRuleTemplate,
            overrides.QueryReservationInfoTemplate ?? defaults.QueryReservationInfoTemplate,
            overrides.ReserveSeatTemplate ?? defaults.ReserveSeatTemplate,
            overrides.CancelReservationTemplate ?? defaults.CancelReservationTemplate,
            overrides.TomorrowReservationQueueUrlTemplate ?? defaults.TomorrowReservationQueueUrlTemplate,
            overrides.TomorrowReservationWarmUpTemplate ?? defaults.TomorrowReservationWarmUpTemplate,
            overrides.TomorrowReservationSaveTemplate ?? defaults.TomorrowReservationSaveTemplate,
            overrides.TomorrowReservationInfoTemplate ?? defaults.TomorrowReservationInfoTemplate);
    }

    public async Task SaveOverridesAsync(TraceIntGraphQlTemplateOverrides overrides, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(overrides, AppJson.Default);

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
    }

    public async Task ResetOverridesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", OverridesKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<TraceIntGraphQlTemplateOverrides> LoadOverridesAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", OverridesKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<TraceIntGraphQlTemplateOverrides>(json, AppJson.Default) ?? new TraceIntGraphQlTemplateOverrides();
        }

        return new TraceIntGraphQlTemplateOverrides();
    }
}
