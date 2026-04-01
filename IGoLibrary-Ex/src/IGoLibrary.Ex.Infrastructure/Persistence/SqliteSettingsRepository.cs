using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

public sealed class SqliteSettingsRepository(SqliteConnectionFactory connectionFactory) : ISettingsRepository
{
    private const string SettingsKey = "app-settings";

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", SettingsKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is string json && !string.IsNullOrWhiteSpace(json))
        {
            var migratedJson = MigrateLegacyAppSettingsJson(json);
            return JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default) ?? AppSettings.Default;
        }

        return AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, AppJson.Default);

        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings(Key, Value)
            VALUES($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", SettingsKey);
        command.Parameters.AddWithValue("$value", json);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string MigrateLegacyAppSettingsJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            document.RootElement.TryGetProperty("customApiOverridesEnabled", out _) ||
            !document.RootElement.TryGetProperty("advancedMode", out _))
        {
            return json;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("advancedMode"))
            {
                writer.WritePropertyName("customApiOverridesEnabled");
                property.Value.WriteTo(writer);
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
