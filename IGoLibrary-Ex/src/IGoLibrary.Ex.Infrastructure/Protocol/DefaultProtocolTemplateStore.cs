using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Infrastructure.Protocol;

public sealed class DefaultProtocolTemplateStore(SqliteConnectionFactory connectionFactory) : IProtocolTemplateStore
{
    private const string OverridesKey = "protocol-overrides";

    public async Task<ProtocolTemplateSet> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var defaults = DefaultTemplates.Instance;
        var overrides = await LoadOverridesAsync(cancellationToken);

        return new ProtocolTemplateSet(
            overrides.GetCookieUrlTemplate ?? defaults.GetCookieUrlTemplate,
            overrides.QueryLibrariesTemplate ?? defaults.QueryLibrariesTemplate,
            overrides.QueryLibraryLayoutTemplate ?? defaults.QueryLibraryLayoutTemplate,
            overrides.QueryLibraryRuleTemplate ?? defaults.QueryLibraryRuleTemplate,
            overrides.QueryReservationInfoTemplate ?? defaults.QueryReservationInfoTemplate,
            overrides.ReserveSeatTemplate ?? defaults.ReserveSeatTemplate,
            overrides.CancelReservationTemplate ?? defaults.CancelReservationTemplate);
    }

    public async Task SaveOverridesAsync(ProtocolTemplateOverrides overrides, CancellationToken cancellationToken = default)
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

    private async Task<ProtocolTemplateOverrides> LoadOverridesAsync(CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM ProtocolOverrides WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", OverridesKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<ProtocolTemplateOverrides>(json, AppJson.Default) ?? new ProtocolTemplateOverrides();
        }

        return new ProtocolTemplateOverrides();
    }
}
