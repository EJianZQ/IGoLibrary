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
            var settings = JsonSerializer.Deserialize<AppSettings>(migratedJson, AppJson.Default) ?? AppSettings.Default;
            return Normalize(settings);
        }

        return AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(Normalize(settings), AppJson.Default);

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
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return json;
        }

        if (!HasLegacySettingsShape(document.RootElement))
        {
            return json;
        }

        var defaults = AppSettings.Default;
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WritePropertyName("notifications");
        writer.WriteStartObject();
        WriteBooleanProperty(
            writer,
            "appBannerNotificationsEnabled",
            document.RootElement,
            "notificationsEnabled",
            defaults.Notifications.AppBannerNotificationsEnabled);
        writer.WritePropertyName("taskEventAlerts");
        if (document.RootElement.TryGetProperty("cookieExpiryAlerts", out var legacyTaskEventAlerts))
        {
            legacyTaskEventAlerts.WriteTo(writer);
        }
        else
        {
            JsonSerializer.Serialize(writer, defaults.Notifications.TaskEventAlerts, AppJson.Default);
        }

        writer.WriteEndObject();

        writer.WritePropertyName("ui");
        writer.WriteStartObject();
        WriteBooleanProperty(writer, "minimizeToTray", document.RootElement, "minimizeToTray", defaults.Ui.MinimizeToTray);
        writer.WritePropertyName("theme");
        writer.WriteStartObject();
        WriteNumberProperty(writer, "mode", document.RootElement, "themeMode", (int)defaults.Ui.Theme!.Mode);
        WriteBooleanProperty(writer, "useSystemAccent", document.RootElement, "useSystemAccent", defaults.Ui.Theme!.UseSystemAccent);
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("protocol");
        writer.WriteStartObject();
        WriteBooleanProperty(
            writer,
            "templateOverridesEnabled",
            document.RootElement,
            "customApiOverridesEnabled",
            document.RootElement.TryGetProperty("advancedMode", out var advancedMode) &&
            advancedMode.ValueKind is JsonValueKind.True);
        writer.WriteEndObject();

        writer.WritePropertyName("requestPolicy");
        writer.WriteStartObject();
        WriteNumberProperty(writer, "timeoutSeconds", document.RootElement, "apiTimeoutSeconds", defaults.RequestPolicy.TimeoutSeconds);
        WriteNumberProperty(writer, "retryCount", document.RootElement, "retryCount", defaults.RequestPolicy.RetryCount);
        writer.WriteEndObject();

        writer.WritePropertyName("tasks");
        writer.WriteStartObject();
        WriteNumberProperty(writer, "grabReservationStrategy", document.RootElement, "grabReservationStrategy", (int)defaults.Tasks.GrabReservationStrategy);
        writer.WriteEndObject();

        writer.WritePropertyName("venue");
        writer.WriteStartObject();
        WriteNullableIntProperty(writer, "lastLibraryId", document.RootElement, "lastLibraryId");
        WriteNullableStringProperty(writer, "lastLibraryName", document.RootElement, "lastLibraryName");
        writer.WriteEndObject();

        writer.WritePropertyName("dashboard");
        writer.WriteStartObject();
        WriteNumberProperty(writer, "successfulReservationCount", document.RootElement, "successfulReservationCount", defaults.Dashboard.SuccessfulReservationCount);
        WriteNumberProperty(writer, "totalGuardSeconds", document.RootElement, "totalGuardSeconds", defaults.Dashboard.TotalGuardSeconds);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var notifications = settings.Notifications ?? NotificationSettings.Default;
        var ui = settings.Ui ?? AppUiSettings.Default;
        var alertSettings = notifications.TaskEventAlerts ?? TaskEventAlertSettings.Default;
        return settings with
        {
            Notifications = notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    alertSettings.Email ?? EmailAlertChannelSettings.Default,
                    alertSettings.Local ?? LocalAlertChannelSettings.Default,
                    alertSettings.Telegram ?? TelegramAlertChannelSettings.Default)
            },
            Ui = ui with
            {
                Theme = ui.Theme ?? ThemeSettings.Default
            },
            Protocol = settings.Protocol ?? ProtocolSettings.Default,
            RequestPolicy = settings.RequestPolicy ?? RequestPolicySettings.Default,
            Tasks = settings.Tasks ?? TaskExecutionSettings.Default,
            Venue = settings.Venue ?? VenueSelectionSettings.Default,
            Dashboard = settings.Dashboard ?? DashboardMetrics.Default
        };
    }

    private static bool HasLegacySettingsShape(JsonElement root)
    {
        return root.TryGetProperty("notificationsEnabled", out _) ||
               root.TryGetProperty("cookieExpiryAlerts", out _) ||
               root.TryGetProperty("minimizeToTray", out _) ||
               root.TryGetProperty("themeMode", out _) ||
               root.TryGetProperty("useSystemAccent", out _) ||
               root.TryGetProperty("customApiOverridesEnabled", out _) ||
               root.TryGetProperty("advancedMode", out _) ||
               root.TryGetProperty("apiTimeoutSeconds", out _) ||
               root.TryGetProperty("retryCount", out _) ||
               root.TryGetProperty("grabReservationStrategy", out _) ||
               root.TryGetProperty("lastLibraryId", out _) ||
               root.TryGetProperty("lastLibraryName", out _) ||
               root.TryGetProperty("successfulReservationCount", out _) ||
               root.TryGetProperty("totalGuardSeconds", out _);
    }

    private static void WriteBooleanProperty(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement root,
        string legacyPropertyName,
        bool defaultValue)
    {
        if (root.TryGetProperty(legacyPropertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            writer.WriteBoolean(propertyName, property.GetBoolean());
            return;
        }

        writer.WriteBoolean(propertyName, defaultValue);
    }

    private static void WriteNumberProperty(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement root,
        string legacyPropertyName,
        int defaultValue)
    {
        if (root.TryGetProperty(legacyPropertyName, out var property))
        {
            if (property.ValueKind is JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                writer.WriteNumber(propertyName, intValue);
                return;
            }

            if (property.ValueKind is JsonValueKind.String && int.TryParse(property.GetString(), out intValue))
            {
                writer.WriteNumber(propertyName, intValue);
                return;
            }
        }

        writer.WriteNumber(propertyName, defaultValue);
    }

    private static void WriteNumberProperty(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement root,
        string legacyPropertyName,
        long defaultValue)
    {
        if (root.TryGetProperty(legacyPropertyName, out var property))
        {
            if (property.ValueKind is JsonValueKind.Number && property.TryGetInt64(out var longValue))
            {
                writer.WriteNumber(propertyName, longValue);
                return;
            }

            if (property.ValueKind is JsonValueKind.String && long.TryParse(property.GetString(), out longValue))
            {
                writer.WriteNumber(propertyName, longValue);
                return;
            }
        }

        writer.WriteNumber(propertyName, defaultValue);
    }

    private static void WriteNullableIntProperty(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement root,
        string legacyPropertyName)
    {
        if (!root.TryGetProperty(legacyPropertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        if (property.ValueKind is JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            writer.WriteNumber(propertyName, intValue);
            return;
        }

        if (property.ValueKind is JsonValueKind.String && int.TryParse(property.GetString(), out intValue))
        {
            writer.WriteNumber(propertyName, intValue);
            return;
        }

        writer.WriteNull(propertyName);
    }

    private static void WriteNullableStringProperty(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement root,
        string legacyPropertyName)
    {
        if (!root.TryGetProperty(legacyPropertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : property.ToString());
    }
}
